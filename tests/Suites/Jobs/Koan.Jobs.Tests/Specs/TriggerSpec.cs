namespace Koan.Jobs.Tests.Specs;

/// <summary>Type-level <c>MyModel.Jobs.Trigger(action)</c> — the on-demand twin of a scheduled tick, with no caller
/// instance. Covers singleton reuse, opt-in coalescing, terminal-doesn't-block-re-trigger, and run-and-wait.</summary>
public sealed class TriggerSpec
{
    [Fact]
    public async Task trigger_runs_a_type_level_action_without_an_instance()
    {
        TickJob.Reset();
        await using var host = await JobsTestHost.StartAsync();

        await TickJob.Jobs.Trigger("sweep");
        await host.Drain();

        TickJob.Executions.Should().Be(1);
        TickJob.LastAction.Should().Be("sweep");
    }

    [Fact]
    public async Task repeated_triggers_reuse_one_singleton_and_each_run()
    {
        TickJob.Reset();
        await using var host = await JobsTestHost.StartAsync();

        await TickJob.Jobs.Trigger("sweep");
        await TickJob.Jobs.Trigger("sweep");   // no [JobIdempotent] → no coalescing → two jobs

        var jobs = await host.Ledger.Query(new JobQuery(WorkType: typeof(TickJob).FullName!), default);
        jobs.Should().HaveCount(2);
        jobs.Select(j => j.WorkId).Distinct().Should().ContainSingle("every trigger reuses the one singleton work-item");

        await host.Drain();
        TickJob.Executions.Should().Be(2);
    }

    [Fact]
    public async Task different_actions_share_the_singleton_but_run_separately()
    {
        TickJob.Reset();
        await using var host = await JobsTestHost.StartAsync();

        await TickJob.Jobs.Trigger("a");
        await TickJob.Jobs.Trigger("b");

        var jobs = await host.Ledger.Query(new JobQuery(WorkType: typeof(TickJob).FullName!), default);
        jobs.Select(j => j.Action).Should().BeEquivalentTo(new[] { "a", "b" });

        await host.Drain();
        TickJob.Executions.Should().Be(2);
    }

    [Fact]
    public async Task overlapping_triggers_coalesce_on_an_idempotent_singleton()
    {
        SweepTick.Reset();
        await using var host = await JobsTestHost.StartAsync();

        await SweepTick.Jobs.Trigger("sweep");
        await SweepTick.Jobs.Trigger("sweep");   // same singleton + key + action → collapses onto the first
        await host.Drain();

        SweepTick.Executions.Should().Be(1);
    }

    [Fact]
    public async Task a_fresh_trigger_after_completion_runs_again()
    {
        SweepTick.Reset();
        await using var host = await JobsTestHost.StartAsync();

        await SweepTick.Jobs.Trigger("sweep");
        await host.Drain();                       // runs, completes (terminal)
        SweepTick.Executions.Should().Be(1);

        await SweepTick.Jobs.Trigger("sweep");    // a terminal row does NOT block a fresh trigger
        await host.Drain();
        SweepTick.Executions.Should().Be(2);
    }

    [Fact]
    public async Task trigger_handle_completion_resolves()
    {
        TickJob.Reset();
        await using var host = await JobsTestHost.StartAsync();

        var handle = await TickJob.Jobs.Trigger("done");
        await host.Drain();

        var outcome = await handle.Completion(TimeSpan.FromSeconds(5));
        outcome.Status.Should().Be(JobStatus.Completed);
        outcome.Succeeded.Should().BeTrue();
    }
}
