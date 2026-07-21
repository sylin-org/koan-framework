using Koan.Data.AdapterSurface.TestKit;

namespace Koan.Data.Connector.Postgres.Tests.Specs.Filtering;

/// <summary>
/// Postgres derivation of the shared filter-convergence oracle (<see cref="FilterConvergence"/>,
/// ARCH-0079). Container-backed (ARCH-0091): skips when Docker is unavailable, otherwise runs every
/// filter through the real Postgres adapter and the in-memory floor and asserts identical id-sets.
/// </summary>
public sealed class PostgresFilterConvergenceSpec(PostgresFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<PostgresFixture>(fixture, output)
{
    [Fact(DisplayName = "Postgres: every filter converges with the in-memory oracle")]
    public async Task Adapter_converges_with_oracle_across_the_corpus()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        await FilterConvergence.AssertConvergesAsync();
    }
}
