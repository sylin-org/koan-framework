using Koan.Data.AdapterSurface.TestKit;

namespace Koan.Data.Connector.SqlServer.Tests.Specs;

/// <summary>
/// DATA-0162 on the real SQL Server adapter: a Get and a Remove over more ids than SQL Server binds as parameters in one
/// statement work with and without a read scope (<see cref="LargeIdentitySets"/>).
/// </summary>
public sealed class SqlServerLargeIdentitySetSpec(SqlServerFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<SqlServerFixture>(fixture, output)
{
    [Fact(DisplayName = "SqlServer: 70,000 ids are read and removed without a read scope")]
    public async Task Unscoped()
    {
        RequireBackingStore();
        await using var host = await BootAsync(LargeIdentitySets.Declare);
        await LargeIdentitySets.AssertUnscopedAsync();
    }

    [Fact(DisplayName = "SqlServer: 70,000 ids are read and removed under a read scope")]
    public async Task Scoped()
    {
        RequireBackingStore();
        await using var host = await BootAsync(LargeIdentitySets.Declare);
        await LargeIdentitySets.AssertScopedAsync();
    }
}
