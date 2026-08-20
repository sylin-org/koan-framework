using Koan.Data.AdapterSurface.TestKit;

namespace Koan.Data.Connector.Mongo.Tests.Specs;

/// <summary>
/// Mongo derivation of the collection-order oracle (<see cref="SortPushdownConvergence"/>, ARCH-0079).
/// Container-backed: skips without Docker. "By each widget's latest sighting" is an aggregate over a nested
/// array, which <c>find</c> cannot sort by at all, so this is also the proof that the pipeline path orders,
/// pages, and materializes correctly — and that the field it adds to sort by does not survive into the entity.
/// </summary>
public sealed class MongoSortPushdownSpec(MongoFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<MongoFixture>(fixture, output)
{
    [Fact(DisplayName = "Mongo: a collection order key is computed by the store, not in memory")]
    public async Task Collection_order_is_pushed_down()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        await SortPushdownConvergence.AssertConvergesAsync(host.Services);
        await SortPushdownConvergence.AssertPagesAsync();
    }
}
