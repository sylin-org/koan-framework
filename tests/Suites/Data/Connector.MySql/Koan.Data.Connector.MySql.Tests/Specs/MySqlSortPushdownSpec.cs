using Koan.Data.AdapterSurface.TestKit;

namespace Koan.Data.Connector.MySql.Tests.Specs;

/// <summary>
/// MySql derivation of the collection-order oracle (<see cref="SortPushdownConvergence"/>, ARCH-0079).
/// Container-backed: skips without a reachable backing store. Proves the store computes "by each widget's
/// latest sighting" itself — JSON_TABLE over the JSON document column — and that its ordering is the one the framework's own sorter would
/// have produced.
/// </summary>
public sealed class MySqlSortPushdownSpec(MySqlFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<MySqlFixture>(fixture, output)
{
    [Fact(DisplayName = "MySql: a collection order key is computed by the store, not in memory")]
    public async Task Collection_order_is_pushed_down()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        await SortPushdownConvergence.AssertScalarOrderingConvergesAsync(host.Services);
        await SortPushdownConvergence.AssertConvergesAsync(host.Services);
        await SortPushdownConvergence.AssertPagesAsync();
        await SortPushdownConvergence.AssertStreamsAsync();
    }
}
