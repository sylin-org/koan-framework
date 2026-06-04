using Koan.Data.Core.Model;

namespace Koan.Jobs.Tests.Specs;

/// <summary>Lane concurrency, level-triggered scheduling (parked + released), and lease reclaim (reaper).</summary>
public sealed class LaneScheduleReapSpec
{
    [Fact]
    public async Task lane_caps_concurrency()
    {
        SlowJob.Reset();
        await using var host = await JobsTestHost.StartAsync();

        var jobs = Enumerable.Range(0, 6).Select(_ => new SlowJob()).ToList();
        await jobs.Submit(SlowJob.Action);
        await host.Drain();

        SlowJob.Peak.Should().BeLessThanOrEqualTo(2, "the lane's MaxConcurrency is 2");
        var done = await host.Coordinator.WhereAsync(new JobQuery(WorkType: typeof(SlowJob).FullName!, Status: JobStatus.Completed), default);
        done.Should().HaveCount(6);
    }

    [Fact]
    public async Task scheduled_action_is_parked_until_its_sweep_releases_it()
    {
        Reconciled.Reset();
        await using var host = await JobsTestHost.StartAsync();
        var r = new Reconciled();

        await r.Job.Submit(Stage.PrepareToFetch);
        await host.Drain();
        Reconciled.Executions.Should().Be(0, "a scheduled action is parked; it does not run on submit");

        await host.ReleaseScheduled();
        await host.Drain();
        Reconciled.Executions.Should().Be(1, "the reconcile sweep releases the parked job");
    }

    [Fact]
    public async Task schedule_respects_its_interval()
    {
        Reconciled.Reset();
        await using var host = await JobsTestHost.StartAsync();

        await new Reconciled().Job.Submit(Stage.PrepareToFetch);
        await host.ReleaseScheduled();   // first sweep releases
        await host.Drain();
        Reconciled.Executions.Should().Be(1);

        await new Reconciled().Job.Submit(Stage.PrepareToFetch);
        await host.ReleaseScheduled();   // interval (10m) not elapsed → not released
        await host.Drain();
        Reconciled.Executions.Should().Be(1);

        host.Advance(TimeSpan.FromMinutes(10));
        await host.ReleaseScheduled();   // interval elapsed → released
        await host.Drain();
        Reconciled.Executions.Should().Be(2);
    }

    [Fact]
    public async Task reaper_reclaims_a_lapsed_lease()
    {
        GreetJob.Reset();
        await using var host = await JobsTestHost.StartAsync();

        // simulate a worker that claimed then died: a Running record with a lapsed lease
        var g = new GreetJob { Name = "recovered" };
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

        await host.Reap();    // Running && lease lapsed → Queued
        await host.Drain();   // re-dispatched and run

        GreetJob.Executions.Should().Be(1);
        (await host.StatusOf<GreetJob>(id)).Should().Be(JobStatus.Completed);
    }
}
