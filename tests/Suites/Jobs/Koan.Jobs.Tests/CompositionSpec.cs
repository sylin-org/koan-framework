using Koan.Core.Diagnostics;
using Koan.Core.Hosting.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Jobs.Tests;

public sealed class CompositionSpec
{
    [Fact]
    public async Task In_memory_host_explains_its_ledger_and_transport()
    {
        await using var host = await JobsHarness.StartInMemoryAsync();

        host.Services.GetRequiredService<IAppRuntime>().Discover();
        var facts = host.Services.GetRequiredService<IKoanRuntimeFacts>().Current.Facts;

        var ledger = facts.Single(fact => fact.Code == "koan.jobs.ledger.selected");
        ledger.Subject.Should().Be("jobs:ledger");
        ledger.Summary.Should().Contain("in-memory");
        ledger.ReasonCode.Should().Be("no-durable-data-adapter");

        var transport = facts.Single(fact => fact.Code == "koan.jobs.transport.selected");
        transport.Subject.Should().Be("jobs:transport");
        transport.Summary.Should().Contain("in-process");
        transport.ReasonCode.Should().Be("default-transport");
    }
}
