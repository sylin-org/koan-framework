using Koan.Data.AdapterSurface.TestKit;

namespace Koan.Data.Connector.Sqlite.Tests.Specs;

/// <summary>Guid and DateTime document filters converge with the CLR oracle (NK-052).</summary>
public sealed class SqliteIdentityValueConvergenceSpec(SqliteFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<SqliteFixture>(fixture, output)
{
    [Fact(DisplayName = "Sqlite: Guid and DateTime filters converge with the CLR oracle")]
    public async Task Guid_and_DateTime_filters_converge()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        await IdentityValueConvergence.AssertConvergesAsync();
    }
}
