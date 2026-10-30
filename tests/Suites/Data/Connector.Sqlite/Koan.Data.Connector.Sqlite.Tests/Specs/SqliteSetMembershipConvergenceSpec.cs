using Koan.Data.AdapterSurface.TestKit;

namespace Koan.Data.Connector.Sqlite.Tests.Specs;

/// <summary>
/// DATA-0162 on the real Sqlite adapter: In and Nin over every scalar type answer what Eq answers member by member, on document
/// fields and typed columns, at every size and with two sets in one statement, executed by the store (<see cref="SetMembershipConvergence"/>).
/// </summary>
public sealed class SqliteSetMembershipConvergenceSpec(SqliteFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<SqliteFixture>(fixture, output)
{
    [Fact(DisplayName = "Sqlite: set filters answer what Eq answers at every size")]
    public async Task Set_filters_converge()
    {
        RequireBackingStore();
        await using var host = await BootAsync(SetMembershipConvergence.Declare);
        await SetMembershipConvergence.AssertConvergesAsync(host.Services);
    }
}
