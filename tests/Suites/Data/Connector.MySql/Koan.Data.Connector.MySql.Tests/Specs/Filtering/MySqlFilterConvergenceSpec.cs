using Koan.Data.AdapterSurface.TestKit;

namespace Koan.Data.Connector.MySql.Tests.Specs.Filtering;

/// <summary>
/// MySQL derivation of the shared filter-convergence oracle (<see cref="FilterConvergence"/>, ARCH-0079).
/// This is the one relational adapter the corpus had never been pointed at (PMC-038).
/// </summary>
public sealed class MySqlFilterConvergenceSpec(MySqlFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<MySqlFixture>(fixture, output)
{
    [Fact(DisplayName = "MySQL: every filter converges with the in-memory oracle")]
    public async Task Adapter_converges_with_oracle_across_the_corpus()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        await FilterConvergence.AssertPushesDownAsync(host.Services);
    }
}
