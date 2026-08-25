using Koan.Jobs.TestKit;

namespace Koan.Jobs.Tests;

/// <summary>JOBS gateway pilot: code-first schedules via <c>MyJob.Jobs.Schedule</c> ride the same engine as
/// attribute-declared schedules.</summary>
public sealed class GatewayScheduleSpec
{
    [Fact]
    public async Task code_first_interval_schedule_fires_on_cadence_and_respects_reset()
    {
        GreetJob.Reset();
        var gateway = GreetJob.Jobs;
        gateway.ResetSchedules();
        gateway.Schedule("", TimeSpan.FromSeconds(2));
        await using var host = await JobsHarness.StartInMemoryAsync();

        await host.TriggerDue();                       // interval: first fire is immediate
        await host.Drain();
        GreetJob.Executions.Should().Be(1);

        host.Advance(TimeSpan.FromSeconds(1));
        await host.TriggerDue();
        await host.Drain();
        GreetJob.Executions.Should().Be(1);            // interval not yet elapsed

        host.Advance(TimeSpan.FromSeconds(1.1));
        await host.TriggerDue();
        await host.Drain();
        GreetJob.Executions.Should().Be(2);

        gateway.ResetSchedules();
        host.Advance(TimeSpan.FromSeconds(3));
        await host.TriggerDue();
        await host.Drain();
        GreetJob.Executions.Should().Be(2);            // reset removes the cadence
    }

    [Fact]
    public async Task code_first_boot_schedule_fires_once_at_boot()
    {
        GreetJob.Reset();
        var gateway = GreetJob.Jobs;
        gateway.ResetSchedules();
        gateway.Schedule("", "@boot");
        await using var host = await JobsHarness.StartInMemoryAsync();

        await host.Boot();
        await host.Drain();
        GreetJob.Executions.Should().Be(1);

        gateway.ResetSchedules();
    }

    [Fact]
    public void conflicting_reregistration_is_corrective_and_identical_is_idempotent()
    {
        var gateway = GreetJob.Jobs;
        gateway.ResetSchedules();

        gateway.Schedule("", "0 9 * * *");
        gateway.Schedule("", "0 9 * * *");             // same expression: idempotent re-entry

        var act = () => gateway.Schedule("", "0 10 * * *");
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*'0 9 * * *'*'0 10 * * *'*");

        gateway.ResetSchedules();
    }
}
