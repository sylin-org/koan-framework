using Koan.Data.AdapterSurface.TestKit;

namespace Koan.Data.Connector.DuckDb.Tests.Specs;

/// <summary>
/// DuckDB derivation of the shared filter-convergence oracle (<see cref="FilterConvergence"/>,
/// ARCH-0079). Dockerless — runs on every build. Every filter is run through the real DuckDB adapter
/// and the in-memory floor; identical id-sets are asserted. This is the spec that caught the
/// correlated-json_each collection-pushdown bug.
/// </summary>
public sealed class DuckDbFilterConvergenceSpec(DuckDbFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<DuckDbFixture>(fixture, output)
{
    [Fact(DisplayName = "DuckDb: every filter converges with the in-memory oracle")]
    public async Task Adapter_converges_with_oracle_across_the_corpus()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        await FilterConvergence.AssertPushesDownAsync(host.Services);
    }
}
