using Koan.Data.AdapterSurface.TestKit;

namespace Koan.Data.Connector.SqlServer.Tests.Specs;

/// <summary>
/// SqlServer derivation of the collection-order oracle (<see cref="SortPushdownConvergence"/>, ARCH-0079).
/// Container-backed: skips without a reachable backing store. Proves the store computes "by each widget's
/// latest sighting" itself — OPENJSON over the nvarchar document column — and that its ordering is the one the framework's own sorter would
/// have produced.
/// </summary>
public sealed class SqlServerSortPushdownSpec(SqlServerFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<SqlServerFixture>(fixture, output)
{
    [Fact(DisplayName = "SqlServer: a collection order key is computed by the store, not in memory")]
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
