using Koan.Data.AdapterSurface.TestKit;

namespace Koan.Data.Connector.DuckDb.Tests.Specs;

/// <summary>
/// DuckDB derivation of the collection-order oracle (<see cref="SortPushdownConvergence"/>, ARCH-0079).
/// Dockerless. Proves DuckDB computes "by each widget's latest sighting" itself, with the ordering the
/// framework's own sorter would have produced.
/// </summary>
public sealed class DuckDbSortPushdownSpec(DuckDbFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<DuckDbFixture>(fixture, output)
{
    [Fact(DisplayName = "DuckDb: a collection order key is computed by the store, not in memory")]
    public async Task Collection_order_is_pushed_down()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        await SortPushdownConvergence.AssertScalarOrderingConvergesAsync(host.Services);
        await SortPushdownConvergence.AssertConvergesAsync(host.Services);
        await SortPushdownConvergence.AssertPagesAsync();
        await SortPushdownConvergence.AssertStreamsAsync();
        await SortPushdownConvergence.AssertNothingFallsBackAsync(host.Services);
    }
}
