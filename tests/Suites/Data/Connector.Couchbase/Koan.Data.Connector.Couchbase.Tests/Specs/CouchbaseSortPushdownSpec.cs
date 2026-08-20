using Koan.Data.AdapterSurface.TestKit;

namespace Koan.Data.Connector.Couchbase.Tests.Specs;

/// <summary>
/// Couchbase derivation of the collection-order oracle (<see cref="SortPushdownConvergence"/>, ARCH-0079).
/// Container-backed: skips without Docker. SQL++ states "by each widget's latest sighting" directly, as
/// <c>ARRAY_MAX</c> over an array comprehension.
/// </summary>
public sealed class CouchbaseSortPushdownSpec(CouchbaseFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<CouchbaseFixture>(fixture, output)
{
    [Fact(DisplayName = "Couchbase: a collection order key is computed by the store, not in memory")]
    public async Task Collection_order_is_pushed_down()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        await SortPushdownConvergence.AssertConvergesAsync(host.Services);
        await SortPushdownConvergence.AssertPagesAsync();
    }
}
