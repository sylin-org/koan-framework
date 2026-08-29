using Koan.Core.Capabilities;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.AdapterSurface.TestKit;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Connector.CouchDb.Tests.Specs;

/// <summary>
/// The honesty contract, locked as behavior: Mango lowers the full scalar set and the collection set,
/// but no sort lowering is claimed (CouchDB gates `_find` sort on a matching index and only `_id` is
/// free), and streaming is not announced — the AODB suite proves the fail-closed rejection.
/// </summary>
public sealed class CouchDbCapabilityTruthSpec(CouchDbFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<CouchDbFixture>(fixture, output)
{
    [Fact]
    public async Task Declared_capabilities_match_what_the_store_can_honor()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        var repo = host.Services.GetRequiredService<IDataService>().GetRepository<ConvergenceWidget, string>();
        var caps = DataCaps.Describe(repo, repo.GetType().Name);

        caps.Has(DataCaps.Query.Filter).Should().BeTrue();
        caps.Has(DataCaps.Isolation.RowScoped).Should().BeTrue();
        caps.Has(DataCaps.Isolation.ContainerScoped).Should().BeTrue();
        caps.Has(DataCaps.Isolation.DatabaseScoped).Should().BeTrue();
        caps.Has(DataCaps.Write.AtomicBatch).Should().BeFalse(
            "_bulk_docs commits per document; atomicity is not claimed");
        caps.Has(DataCaps.Query.ProviderBoundedPaging).Should().BeFalse(
            "Mango has no server-side cursor; streaming is not announced");

        var support = caps.Detail<FilterSupport>(DataCaps.Query.Filter);
        support.Should().NotBeNull();
        support!.ScalarOperators.Should().Contain(FilterOperator.Eq);
        support.CollectionOperators.Should().Contain(FilterOperator.HasAll)
            .And.NotContain(FilterOperator.Eq,
                "bare equality against an array element does not match Mango semantics and rides the floor");
    }

    [Fact]
    public async Task Pages_stay_exact_through_the_declared_sort_fallback()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        // Sorts are index-gated on CouchDB, so a collection-aggregate page rides the framework's
        // declared fallback — the windows must still be exact, which is what the oracle asserts.
        await SortPushdownConvergence.AssertPagesAsync();
    }
}
