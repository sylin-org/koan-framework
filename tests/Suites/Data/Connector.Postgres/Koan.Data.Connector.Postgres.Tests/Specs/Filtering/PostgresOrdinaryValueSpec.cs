using Koan.Data.AdapterSurface.TestKit;

namespace Koan.Data.Connector.Postgres.Tests.Specs.Filtering;

/// <summary>Ordinary numbers read back as written, a large double jsonb prints whole among them (DATA-0164).</summary>
public sealed class PostgresOrdinaryValueSpec(PostgresFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<PostgresFixture>(fixture, output)
{
    [Fact(DisplayName = "Postgres: doubles and ordinary decimals read back as written")]
    public async Task Numbers_round_trip()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        await OrdinaryValueConvergence.AssertNumbersRoundTripAsync(7.9e28);
    }

    [Fact(DisplayName = "Postgres: ids differing only by case or accent stay separate rows")]
    public async Task Ids_stay_distinct()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        await OrdinaryValueConvergence.AssertIdsStayDistinctAsync();
    }
}
