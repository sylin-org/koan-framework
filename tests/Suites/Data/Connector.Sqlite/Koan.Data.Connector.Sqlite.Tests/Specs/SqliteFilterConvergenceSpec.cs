using Koan.Data.AdapterSurface.TestKit;

namespace Koan.Data.Connector.Sqlite.Tests.Specs;

/// <summary>
/// SQLite derivation of the shared filter-convergence oracle (<see cref="FilterConvergence"/>,
/// ARCH-0079). Dockerless — runs on every build. Every filter is run through the real SQLite adapter
/// and the in-memory floor; identical id-sets are asserted. This is the spec that caught the
/// correlated-json_each collection-pushdown bug.
/// </summary>
public sealed class SqliteFilterConvergenceSpec(SqliteFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<SqliteFixture>(fixture, output)
{
    [Fact(DisplayName = "Sqlite: every filter converges with the in-memory oracle")]
    public async Task Adapter_converges_with_oracle_across_the_corpus()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        await FilterConvergence.AssertConvergesAsync();
    }
}
