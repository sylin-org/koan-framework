using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Abstractions.Filtering;
using Koan.Core.Capabilities;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Connector.Json.Tests.Specs.Capabilities;

public sealed class JsonCapabilitiesSpec(JsonFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<JsonFixture>(fixture, output)
{
    [Fact]
    public async Task Repository_reports_linq_and_single_replacement_bulk_writes()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        var data = host.Services.GetRequiredService<IDataService>();

        var repository = data.GetRepository<CapabilityProbe, string>();
        repository.Should().BeAssignableTo<IQueryRepository<CapabilityProbe, string>>();

        // A JSON bulk mutation produces one physical file replacement, not one replacement per row.
        var caps = DataCaps.Describe(repository, repository.GetType().Name);
        caps.Has(DataCaps.Query.Linq).Should().BeTrue();
        caps.Has(DataCaps.Query.String).Should().BeFalse();
        caps.Has(DataCaps.Write.BulkUpsert).Should().BeTrue();
        caps.Has(DataCaps.Write.BulkDelete).Should().BeTrue();
        caps.Has(DataCaps.Write.AtomicBatch).Should().BeFalse();
        caps.Has(DataCaps.Write.FastRemove).Should().BeFalse();

        // The KeyValue family evaluates the whole filter AST over loaded records and declares
        // FilterSupport.Full on that basis, so the collection-element substring operator is claimed too.
        var filterSupport = caps.Detail<FilterSupport>(DataCaps.Query.Filter) ?? FilterSupport.None;
        filterSupport.CollectionOperators.Should().Contain(FilterOperator.HasContains);

        var published = new ClaimCapture();
        new JsonAdapterFactory().DescribeClaims(published);
        published.Capabilities.Should().Contain([
            DataCaps.Query.Linq,
            DataCaps.Query.Filter,
            DataCaps.Write.BulkUpsert,
            DataCaps.Write.BulkDelete,
            DataCaps.Isolation.RowScoped,
            DataCaps.Isolation.ContainerScoped,
            DataCaps.Isolation.DatabaseScoped
        ]);
        published.Capabilities.Should().NotContain(DataCaps.Write.AtomicBatch);

        var partition = NewPartition("capabilities");
        using var lease = Lease(partition);

        await CapabilityProbe.Upsert(new CapabilityProbe { Name = "cap" });
        var all = await CapabilityProbe.All();
        all.Should().ContainSingle(p => p.Name == "cap");
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
