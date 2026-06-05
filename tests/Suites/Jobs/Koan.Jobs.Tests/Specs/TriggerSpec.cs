namespace Koan.Jobs.Tests.Specs;

/// <summary>Type-level <c>MyModel.Jobs.Trigger(action)</c> — the on-demand twin of a scheduled tick, with no caller instance.</summary>
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
    public async Task overlapping_triggers_coalesce_on_an_idempotent_singleton()
    {
        SweepTick.Reset();
        await using var host = await JobsTestHost.StartAsync();

        await SweepTick.Jobs.Trigger("sweep");
        await SweepTick.Jobs.Trigger("sweep");   // same singleton + key + action → collapses onto the first
        await host.Drain();

        SweepTick.Executions.Should().Be(1);
    }
}
