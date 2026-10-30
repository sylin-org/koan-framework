using Koan.Data.AdapterSurface.TestKit;

namespace Koan.Data.Connector.Sqlite.Tests.Specs;

/// <summary>Ordinary numbers read back as written, and a large double saves (DATA-0164).</summary>
public sealed class SqliteOrdinaryValueSpec(SqliteFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<SqliteFixture>(fixture, output)
{
    [Fact(DisplayName = "Sqlite: doubles and ordinary decimals read back as written")]
    public async Task Numbers_round_trip()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        await OrdinaryValueConvergence.AssertNumbersRoundTripAsync(7.9e28, double.MaxValue);
    }

    [Fact(DisplayName = "Sqlite: ids differing only by case or accent stay separate rows")]
    public async Task Ids_stay_distinct()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        await OrdinaryValueConvergence.AssertIdsStayDistinctAsync();
    }
}
