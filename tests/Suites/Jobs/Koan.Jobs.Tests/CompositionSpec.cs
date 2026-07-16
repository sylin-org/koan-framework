using Koan.Core.Diagnostics;
using Koan.Core.Hosting.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Jobs.Tests;

public sealed class CompositionSpec
{
    [Fact]
    public async Task In_memory_host_explains_its_ledger_and_communication_wake()
    {
        await using var host = await JobsHarness.StartInMemoryAsync();

        host.Services.GetRequiredService<IAppRuntime>().Discover();
        var facts = host.Services.GetRequiredService<IKoanRuntimeFacts>().Current.Facts;

        var ledger = facts.Single(fact => fact.Code == "koan.jobs.ledger.selected");
        ledger.Subject.Should().Be("jobs:ledger");
        ledger.Summary.Should().Contain("in-memory");
        ledger.ReasonCode.Should().Be("no-durable-data-adapter");

        var wake = facts.Single(fact => fact.Code == "koan.jobs.wake.selected");
        wake.Subject.Should().Be("jobs:wake");
        wake.Summary.Should().Contain("in-process");
        wake.ReasonCode.Should().Be("ledger-backed-latency-hint");

        facts.Should().Contain(fact =>
            fact.Code == "koan.communication.framework-signals.selected"
            && fact.Subject == "communication:framework-signals:default"
            && fact.ReasonCode == "built-in-floor");
    }
}
