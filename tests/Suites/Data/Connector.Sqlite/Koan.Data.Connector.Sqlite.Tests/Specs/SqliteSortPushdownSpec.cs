using Koan.Data.AdapterSurface.TestKit;

namespace Koan.Data.Connector.Sqlite.Tests.Specs;

/// <summary>
/// SQLite derivation of the collection-order oracle (<see cref="SortPushdownConvergence"/>, ARCH-0079).
/// Dockerless. Proves SQLite computes "by each widget's latest sighting" itself, with the ordering the
/// framework's own sorter would have produced.
/// </summary>
public sealed class SqliteSortPushdownSpec(SqliteFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<SqliteFixture>(fixture, output)
{
    [Fact(DisplayName = "Sqlite: a collection order key is computed by the store, not in memory")]
    public async Task Collection_order_is_pushed_down()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        await SortPushdownConvergence.AssertScalarOrderingConvergesAsync(host.Services);
        await SortPushdownConvergence.AssertConvergesAsync(host.Services);
        await SortPushdownConvergence.AssertPagesAsync();
        await SortPushdownConvergence.AssertUnsortedPagesPartitionTheCorpusAsync();
        await SortPushdownConvergence.AssertStreamsAsync();
        await SortPushdownConvergence.AssertNothingFallsBackAsync(host.Services);
    }
}
