namespace Koan.Jobs.Tests.Specs;

/// <summary>Cooperative backoff: reschedule (no attempt consumed), runaway guard, and the shared resource gate.</summary>
public sealed class BackoffSpec
{
    [Fact]
    public async Task reschedule_defers_without_consuming_an_attempt()
    {
        RescheduleJob.Reset();
        RescheduleJob.RescheduleUntil = 2;
        await using var host = await JobsTestHost.StartAsync();
        var j = new RescheduleJob();
        var id = j.Id;

        await j.Job.Submit();
        await host.Drain();                                   // run 1 → reschedules
        RescheduleJob.Executions.Should().Be(1);

        await host.Drain();                                   // deferred → nothing ready
        RescheduleJob.Executions.Should().Be(1);

        host.Advance(TimeSpan.FromMinutes(5));
        await host.Drain();                                   // run 2 → reschedules
        host.Advance(TimeSpan.FromMinutes(5));
        await host.Drain();                                   // run 3 → completes

        RescheduleJob.Executions.Should().Be(3);
        var rec = await host.JobFor<RescheduleJob>(id);
        rec!.Status.Should().Be(JobStatus.Completed);
        rec.Reschedules.Should().Be(2);
        rec.Attempt.Should().Be(1, "reschedules must not consume retry attempts");
    }

    [Fact]
    public async Task max_reschedules_dead_letters()
    {
        RescheduleJob.Reset();
        RescheduleJob.RescheduleUntil = 99; // always reschedule
        await using var host = await JobsTestHost.StartAsync(o => o.DefaultMaxReschedules = 1);
        var j = new RescheduleJob();
        var id = j.Id;

        await j.Job.Submit();
        await host.Drain();                                   // Reschedules 0→1 (1 > 1 = false) → deferred
        host.Advance(TimeSpan.FromMinutes(5));
        await host.Drain();                                   // Reschedules 1→2 (2 > 1 = true) → Dead

        var rec = await host.JobFor<RescheduleJob>(id);
        rec!.Status.Should().Be(JobStatus.Dead);
        rec.DeadReason.Should().Be(nameof(DeadReason.PerpetuallyDeferred));
    }

    [Fact]
    public async Task backoff_gate_defers_peers_at_dispatch_without_running()
    {
        GatedJob.Reset();
        GatedJob.Trip429 = true;
        await using var host = await JobsTestHost.StartAsync();

        var a = new GatedJob { Host = "api" };
        var b = new GatedJob { Host = "api" };
        var c = new GatedJob { Host = "api" };

        await a.Job.Submit();
        await host.Drain();                                   // a trips the 429 and gates "api"
        GatedJob.Executions.Should().Be(1);

        await b.Job.Submit();
        await c.Job.Submit();
        await host.Drain();                                   // b, c are gated at dispatch → never run
        GatedJob.Executions.Should().Be(1, "peers for the gated host defer at dispatch without running");

        host.Advance(TimeSpan.FromMinutes(5));
        await host.Drain();                                   // gate released → all run to completion

        (await host.StatusOf<GatedJob>(a.Id)).Should().Be(JobStatus.Completed);
        (await host.StatusOf<GatedJob>(b.Id)).Should().Be(JobStatus.Completed);
        (await host.StatusOf<GatedJob>(c.Id)).Should().Be(JobStatus.Completed);
    }
}
