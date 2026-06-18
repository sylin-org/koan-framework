using Koan.Data.AdapterSurface.TestKit;

namespace Koan.Data.Connector.SqlServer.Tests.Specs.Filtering;

/// <summary>
/// SqlServer derivation of the shared filter-convergence oracle (<see cref="FilterConvergence"/>,
/// ARCH-0079). Container-backed (ARCH-0091): skips when no SQL Server is reachable, otherwise runs every
/// filter through the real SqlServer adapter and the in-memory floor and asserts identical id-sets.
/// </summary>
public sealed class SqlServerFilterConvergenceSpec(SqlServerFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<SqlServerFixture>(fixture, output)
{
    [Fact(DisplayName = "SqlServer: every filter converges with the in-memory oracle")]
    public async Task Adapter_converges_with_oracle_across_the_corpus()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        await FilterConvergence.AssertConvergesAsync();
    }
}
