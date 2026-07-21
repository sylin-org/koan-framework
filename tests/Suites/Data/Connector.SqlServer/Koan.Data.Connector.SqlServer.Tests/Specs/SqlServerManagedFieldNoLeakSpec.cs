using Koan.Data.AdapterSurface.TestKit;

namespace Koan.Data.Connector.SqlServer.Tests.Specs;

/// <summary>
/// SQL Server derivation of the managed-field isolation oracle (<see cref="ManagedFieldNoLeak"/>, DATA-0105 §3b /
/// ARCH-0079) — the tenancy substrate proven on the real SQL Server adapter: the invisible discriminator injected
/// into the [Json] envelope isolates reads, defends get-by-id (IDOR), rejects a cross-scope id-keyed upsert (the
/// <c>MERGE … WHEN MATCHED AND JSON_VALUE([Json],'$.…') = @scope</c> guard ⇒ 0 rows ⇒ reject), and scopes RemoveAll.
/// </summary>
public sealed class SqlServerManagedFieldNoLeakSpec(SqlServerFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<SqlServerFixture>(fixture, output)
{
    [Fact(DisplayName = "SqlServer: the managed-field discriminator isolates reads/writes/deletes (no leak)")]
    public async Task Managed_field_isolation_holds()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        await ManagedFieldNoLeak.AssertNoLeakAsync();
    }
}
