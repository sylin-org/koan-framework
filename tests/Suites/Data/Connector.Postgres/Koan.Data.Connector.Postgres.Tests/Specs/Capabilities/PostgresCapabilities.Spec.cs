using Koan.Data.Abstractions.Capabilities;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Connector.Postgres.Tests.Specs.Capabilities;

public sealed class PostgresCapabilitiesSpec(PostgresFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<PostgresFixture>(fixture, output)
{
    [Fact]
    public async Task Repository_reports_expected_capabilities()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        var data = host.Services.GetRequiredService<IDataService>();

        var repo = data.GetRepository<CapabilityProbe, string>();
        repo.Should().BeAssignableTo<IQueryRepository<CapabilityProbe, string>>();
        repo.Should().BeAssignableTo<IRawQueryRepository<CapabilityProbe, string>>();

        // ARCH-0084: negotiate via the unified CapabilitySet.
        var caps = DataCaps.Describe(repo, repo.GetType().Name);
        caps.Has(DataCaps.Query.Linq).Should().BeTrue();
        caps.Has(DataCaps.Query.String).Should().BeTrue();
        caps.Has(DataCaps.Write.AtomicBatch).Should().BeTrue();
        caps.Has(DataCaps.Write.BulkDelete).Should().BeTrue();
        caps.Has(DataCaps.Write.FastRemove).Should().BeTrue();
        caps.Has(DataCaps.Write.BulkUpsert).Should().BeTrue();

        var partition = NewPartition();
        using var lease = Lease(partition);

        await CapabilityProbe.Upsert(new CapabilityProbe { Name = "cap" });
        var count = await CapabilityProbe.Count.Exact();
        count.Should().Be(1);

        var linqQuery = await CapabilityProbe.Query(p => p.Name == "cap");
        linqQuery.Should().ContainSingle();
    }

    private sealed class CapabilityProbe : Entity<CapabilityProbe>
    {
        public string Name { get; set; } = "";
    }
}
