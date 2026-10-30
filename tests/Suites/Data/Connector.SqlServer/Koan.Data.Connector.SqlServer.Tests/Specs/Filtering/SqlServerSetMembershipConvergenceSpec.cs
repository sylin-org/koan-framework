using Koan.Data.AdapterSurface.TestKit;

namespace Koan.Data.Connector.SqlServer.Tests.Specs.Filtering;

/// <summary>
/// DATA-0162 on the real SqlServer adapter: In and Nin over every scalar type answer what Eq answers member by member, on document
/// fields and typed columns, at every size and with two sets in one statement, executed by the store (<see cref="SetMembershipConvergence"/>).
/// </summary>
public sealed class SqlServerSetMembershipConvergenceSpec(SqlServerFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<SqlServerFixture>(fixture, output)
{
    [Fact(DisplayName = "SqlServer: set filters answer what Eq answers at every size")]
    public async Task Set_filters_converge()
    {
        RequireBackingStore();
        await using var host = await BootAsync(SetMembershipConvergence.Declaration(offsetColumns: true, clockColumns: false));
        await SetMembershipConvergence.AssertConvergesAsync(host.Services, offsetColumns: true, clockColumns: false);
    }
}
