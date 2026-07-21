using AwesomeAssertions;
using Koan.Core.Diagnostics;
using Koan.Core.Hosting.Runtime;
using Koan.Jobs.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Jobs.Adapter.Sqlite.Tests.Specs;

public sealed class CompositionSpec
{
    [Fact]
    public async Task Sqlite_host_explains_its_durable_ledger()
    {
        await using var host = await JobsHarness.StartSqliteAsync();

        host.Services.GetRequiredService<IAppRuntime>().Discover();
        var facts = host.Services.GetRequiredService<IKoanRuntimeFacts>().Current.Facts;

        var ledger = facts.Single(fact => fact.Code == "koan.jobs.ledger.selected");
        ledger.Subject.Should().Be("jobs:ledger");
        ledger.Summary.Should().Contain("durable-data");
        ledger.ReasonCode.Should().Be("durable-data-adapter");

        var wake = facts.Single(fact => fact.Code == "koan.jobs.wake.selected");
        wake.Subject.Should().Be("jobs:wake");
        wake.Summary.Should().Contain("in-process");
        wake.ReasonCode.Should().Be("ledger-backed-latency-hint");
    }
}
