using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Abstractions.Filtering;
using Koan.Core.Capabilities;
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

        // Postgres lowers collection-element substring natively (jsonb_array_elements_text LIKE) and says so.
        var filterSupport = caps.Detail<FilterSupport>(DataCaps.Query.Filter) ?? FilterSupport.None;
        filterSupport.CollectionOperators.Should().Contain(FilterOperator.HasContains);

        var factory = new PostgresAdapterFactory();
        factory.ReferenceIdentities.Should().Contain("Koan.Data.Connector.Postgres");
        var published = new ClaimCapture();
        factory.DescribeClaims(published);
        published.Capabilities.Should().Contain([
            DataCaps.Query.Linq,
            DataCaps.Query.String,
            DataCaps.Query.Filter,
            DataCaps.Write.AtomicBatch,
            DataCaps.Write.ConditionalReplace,
            DataCaps.Isolation.RowScoped,
            DataCaps.Isolation.ContainerScoped,
            DataCaps.Isolation.DatabaseScoped
        ]);

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

    private sealed class ClaimCapture : IDataClaims
    {
        public HashSet<Capability> Capabilities { get; } = [];
        public IDataClaims Profile(string profile, string? qualifier = null, bool advertised = true) => this;
        public IDataClaims Capability(Capability capability, bool advertised = true)
        {
            if (advertised) Capabilities.Add(capability);
            return this;
        }
    }
}
