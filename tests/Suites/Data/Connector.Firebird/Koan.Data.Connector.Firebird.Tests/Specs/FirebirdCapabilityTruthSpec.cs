using Koan.Core.Capabilities;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.AdapterSurface.TestKit;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Connector.Firebird.Tests.Specs;

/// <summary>
/// The honesty contract, locked as behavior: the adapter announces exactly the capabilities a real
/// Firebird 5 store can honor. Streaming is absent (the AODB suite proves the fail-closed rejection),
/// and the filter support carries no collection operators, because no JSON functions exist to lower
/// them with. Over-claiming here is how a silent in-memory fallback gets born.
/// </summary>
public sealed class FirebirdCapabilityTruthSpec(FirebirdFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<FirebirdFixture>(fixture, output)
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
        caps.Has(DataCaps.Write.AtomicBatch).Should().BeTrue();
        caps.Has(DataCaps.Query.ProviderBoundedPaging).Should().BeFalse(
            "streaming is not announced on this store; the adapter must reject streams, not materialize");

        var support = caps.Detail<FilterSupport>(DataCaps.Query.Filter);
        support.Should().NotBeNull();
        support!.CollectionOperators.Should().BeEmpty(
            "Firebird has no JSON functions; collection operators must ride the declared floor");
        support.ScalarOperators.Should().Contain(FilterOperator.Eq);
        support.NestedPaths.Should().BeFalse();
    }
}
