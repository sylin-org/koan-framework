using Koan.Data.AdapterSurface.TestKit;

namespace Koan.Data.Connector.MySql.Tests.Specs.Filtering;

/// <summary>Ordinary numbers read back as written, and ids differing by case stay two rows (DATA-0164).</summary>
public sealed class MySqlOrdinaryValueSpec(MySqlFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<MySqlFixture>(fixture, output)
{
    [Fact(DisplayName = "MySQL: doubles and ordinary decimals read back as written")]
    public async Task Numbers_round_trip()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        await OrdinaryValueConvergence.AssertNumbersRoundTripAsync();
    }

    [Fact(DisplayName = "MySQL: ids differing only by case or accent stay separate rows")]
    public async Task Ids_stay_distinct()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        await OrdinaryValueConvergence.AssertIdsStayDistinctAsync();
    }
}
