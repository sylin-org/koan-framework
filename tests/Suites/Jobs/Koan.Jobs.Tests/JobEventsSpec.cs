using Koan.Jobs.TestKit;

namespace Koan.Jobs.Tests;

/// <summary>PMC-056: lifecycle events are pure projection — past-participle observers, fired after
/// the durable write, throw-safe, never participants in settlement.</summary>
public sealed class JobEventsSpec
{
    [Fact]
    public async Task completed_event_fires_with_typed_model()
    {
        GreetJob.Reset();
        GreetJob.Jobs.ResetEvents();
        JobEvent<GreetJob>? seen = null;
        GreetJob.Jobs.OnCompleted(e => seen = e);

        await using var host = await JobsHarness.StartInMemoryAsync();
        var g = new GreetJob { Name = "observed" };
        var id = g.Id;
        await g.Job.Submit("");
        await host.Drain();

        (await host.StatusOf<GreetJob>(id)).Should().Be(JobStatus.Completed);
        seen.Should().NotBeNull("the completed settlement projects an event");
        seen!.Kind.Should().Be(JobEventKind.Completed);
        seen.Model.Should().NotBeNull();
        seen.Model!.Id.Should().Be(id);
        seen.Record.WorkType.Should().Be(typeof(GreetJob).FullName);
    }

    [Fact]
    public async Task failed_event_fires_on_terminal_failure()
    {
        FlakyJob.Reset();
        FlakyJob.SucceedAtAttempt = 99;   // never succeeds; MaxAttempts=3 on the attribute
        FlakyJob.Jobs.ResetEvents();
        JobEvent<FlakyJob>? seen = null;
        FlakyJob.Jobs.OnFailed(e => seen = e);

        await using var host = await JobsHarness.StartInMemoryAsync();
        var f = new FlakyJob();
        var id = f.Id;
        await f.Job.Submit("");
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await host.Drain();
            host.Advance(TimeSpan.FromMinutes(1));
            await host.Drain();
        }

        (await host.StatusOf<FlakyJob>(id)).Should().Be(JobStatus.Failed);
        seen.Should().NotBeNull();
        seen!.Kind.Should().Be(JobEventKind.Failed);
        seen.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task stalled_event_fires_on_reaper_reclaim()
    {
        GreetJob.Reset();
        GreetJob.Jobs.ResetEvents();
        JobEvent<GreetJob>? stalled = null;
        GreetJob.Jobs.OnStalled(e => stalled = e);

        await using var host = await JobsHarness.StartInMemoryAsync();
        var g = new GreetJob { Name = "lapsed" };
        var id = g.Id;
        await GreetJob.Upsert(g);
        var now = host.Clock.GetUtcNow();
        await host.Ledger.Append(new JobRecord
        {
            WorkType = typeof(GreetJob).FullName!,
            WorkId = id,
            Action = "",
            Status = JobStatus.Running,
            Attempt = 1,
            Lane = "default",
            FirstSubmittedAt = now,
            VisibleAt = now,
            LeaseUntil = now - TimeSpan.FromMinutes(1),
            Owner = "dead-worker",
        }, default);

        await host.Reap();

        stalled.Should().NotBeNull("the reaper reclaim projects a stalled event");
        stalled!.Record.Status.Should().Be(JobStatus.Queued);
    }

    [Fact]
    public async Task throwing_observer_never_affects_settlement()
    {
        GreetJob.Reset();
        GreetJob.Jobs.ResetEvents();
        GreetJob.Jobs.OnCompleted(_ => throw new InvalidOperationException("observer bomb"));

        await using var host = await JobsHarness.StartInMemoryAsync();
        var g = new GreetJob { Name = "resilient" };
        var id = g.Id;
        await g.Job.Submit("");
        await host.Drain();

        (await host.StatusOf<GreetJob>(id)).Should().Be(JobStatus.Completed, "observers are projections; settlement is untouched");
    }

    [Fact]
    public async Task reset_events_clears_observers()
    {
        GreetJob.Reset();
        GreetJob.Jobs.ResetEvents();
        var fired = 0;
        GreetJob.Jobs.OnCompleted(_ => fired++);
        GreetJob.Jobs.ResetEvents();

        await using var host = await JobsHarness.StartInMemoryAsync();
        var g = new GreetJob { Name = "quiet" };
        await g.Job.Submit("");
        await host.Drain();

        fired.Should().Be(0);
    }

    /// <summary>PMC-056 proof: every observer is a projection of the persisted transition audit, so its firing
    /// count must equal the matching transitions exactly — once per claim on a retrying job, once per completion,
    /// zero otherwise. Double-projection (or none) means the event layer has its own write path, which it must not.</summary>
    [Fact]
    public async Task each_transition_projects_its_observer_exactly_once()
    {
        FlakyJob.Reset();
        FlakyJob.SucceedAtAttempt = 2;   // attempt 1 fails → retry; attempt 2 completes
        GreetJob.Reset();
        FlakyJob.Jobs.ResetEvents();
        GreetJob.Jobs.ResetEvents();
        var flakyClaimed = 0;
        var flakyFailed = 0;
        var flakyCompleted = 0;
        var flakyStalled = 0;
        var flakyAbandoned = 0;
        var flakyRescheduled = 0;
        FlakyJob.Jobs.OnClaimed(_ => flakyClaimed++);
        FlakyJob.Jobs.OnFailed(_ => flakyFailed++);
        FlakyJob.Jobs.OnCompleted(_ => flakyCompleted++);
        FlakyJob.Jobs.OnStalled(_ => flakyStalled++);
        FlakyJob.Jobs.OnAbandoned(_ => flakyAbandoned++);
        FlakyJob.Jobs.OnRescheduled(_ => flakyRescheduled++);

        await using var host = await JobsHarness.StartInMemoryAsync();
        var f = new FlakyJob();
        var id = f.Id;
        await f.Job.Submit("");

        for (var attempt = 0; attempt < 3; attempt++)
        {
            await host.Drain();
            host.Advance(TimeSpan.FromMinutes(1));   // release the retry's VisibleAt
            await host.Drain();
        }

        (await host.StatusOf<FlakyJob>(id)).Should().Be(JobStatus.Completed);
        var record = (await host.JobFor<FlakyJob>(id))!;

        // The audit for one failing-then-succeeding run: two claims, one retry re-queue, one completion.
        // (Submit itself seeds a Created→Queued row; the re-queue is isolated by its Running origin.)
        record.Transitions.Count(t => t.Note!.StartsWith("claimed by")).Should().Be(2, "two real claims happened");
        record.Transitions.Count(t => t.To == JobStatus.Queued && t.From == JobStatus.Running)
            .Should().Be(1, "the single retry re-queue");
        record.Transitions.Count(t => t.To == JobStatus.Completed).Should().Be(1);

        flakyClaimed.Should().Be(record.Transitions.Count(t => t.Note!.StartsWith("claimed by")),
            "the claimed projection mirrors the claim audit exactly");
        flakyCompleted.Should().Be(record.Transitions.Count(t => t.To == JobStatus.Completed));
        flakyFailed.Should().Be(0, "the job recovered; no terminal failure ever existed");
        flakyRescheduled.Should().Be(0, "a plain retry is not a cooperative reschedule");
        flakyStalled.Should().Be(0);
        flakyAbandoned.Should().Be(0);
    }
}
