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
    public async Task scheduled_action_runs_when_the_scheduler_ticks()
    {
        Reconciled.Reset();
        await using var host = await JobsTestHost.StartAsync();

        await host.Drain();
        Reconciled.Executions.Should().Be(0, "nothing runs until the scheduler submits it");

        await host.TriggerDue();   // first tick submits a fresh job
        await host.Drain();
        Reconciled.Executions.Should().Be(1);
    }

    [Fact]
    public async Task recurring_schedule_actually_recurs()
    {
        Reconciled.Reset();
        await using var host = await JobsTestHost.StartAsync();

        await host.TriggerDue(); await host.Drain();                                         // t0 → 1
        host.Advance(TimeSpan.FromMinutes(10)); await host.TriggerDue(); await host.Drain(); // → 2
        host.Advance(TimeSpan.FromMinutes(10)); await host.TriggerDue(); await host.Drain(); // → 3

        Reconciled.Executions.Should().Be(3, "each interval the scheduler submits a fresh job");
    }

    [Fact]
    public async Task schedule_respects_its_interval()
    {
        Reconciled.Reset();
        await using var host = await JobsTestHost.StartAsync();

        await host.TriggerDue(); await host.Drain();   // t0 → 1
        Reconciled.Executions.Should().Be(1);

        await host.TriggerDue(); await host.Drain();   // immediate: interval (10m) not elapsed → no submit
        Reconciled.Executions.Should().Be(1);

        host.Advance(TimeSpan.FromMinutes(10));
        await host.TriggerDue(); await host.Drain();   // due → 2
        Reconciled.Executions.Should().Be(2);
    }

    [Fact]
    public async Task continuous_schedule_fires_every_tick()
    {
        Heartbeat.Reset();
        await using var host = await JobsTestHost.StartAsync();

        await host.TriggerDue(); await host.Drain();
        await host.TriggerDue(); await host.Drain();   // @continuous: no interval gate
        await host.TriggerDue(); await host.Drain();

        Heartbeat.Executions.Should().Be(3);
    }

    [Fact]
    public async Task boot_action_runs_once_at_boot()
    {
        BootOnce.Reset();
        await using var host = await JobsTestHost.StartAsync();

        await host.Boot();
        await host.Drain();

        BootOnce.Executions.Should().Be(1);
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
