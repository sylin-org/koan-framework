using System;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Capabilities;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Connector.InMemory.Tests.Specs.Capabilities;

public sealed class InMemoryCapabilitiesSpec(InMemoryFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<InMemoryFixture>(fixture, output)
{
    [Fact]
    public async Task Repository_reports_linq_and_atomic_write_capabilities()
    {
        RequireBackingStore();
        await using var host = await BootAsync();

        var data = host.Services.GetRequiredService<IDataService>();
        var repo = data.GetRepository<CapabilityProbe, string>();
        repo.Should().BeAssignableTo<IQueryRepository<CapabilityProbe, string>>();

        // ARCH-0084: negotiate via the unified CapabilitySet (verifies the facade forwards
        // the inner adapter's declaration through IDescribesCapabilities).
        var caps = DataCaps.Describe(repo, repo.GetType().Name);
        caps.Has(DataCaps.Query.Linq).Should().BeTrue();
        caps.Has(DataCaps.Query.String).Should().BeFalse();
        caps.Has(DataCaps.Write.BulkUpsert).Should().BeTrue();
        caps.Has(DataCaps.Write.BulkDelete).Should().BeTrue();
        caps.Has(DataCaps.Write.AtomicBatch).Should().BeTrue();

        await CapabilityProbe.Upsert(new CapabilityProbe { Name = "cap" });
        var count = await CapabilityProbe.Count.Exact();
        count.Should().Be(1);
    }

    private sealed class CapabilityProbe : Entity<CapabilityProbe>
    {
        public string Name { get; set; } = "";
    }
}
