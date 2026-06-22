using Koan.Data.AdapterSurface.TestKit;

namespace Koan.Data.Connector.Postgres.Tests.Specs;

/// <summary>
/// Postgres derivation of the managed-field isolation oracle (<see cref="ManagedFieldNoLeak"/>, DATA-0105 §3b /
/// ARCH-0079) — the tenancy substrate proven on the real Postgres adapter: the invisible discriminator injected
/// into the jsonb envelope isolates reads, defends get-by-id (IDOR), rejects a cross-scope id-keyed upsert
/// (the <c>ON CONFLICT … WHERE ("Json" #&gt;&gt; '{…}') = @scope</c> guard), and scopes RemoveAll.
/// </summary>
public sealed class PostgresManagedFieldNoLeakSpec(PostgresFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<PostgresFixture>(fixture, output)
{
    [Fact(DisplayName = "Postgres: the managed-field discriminator isolates reads/writes/deletes (no leak)")]
    public async Task Managed_field_isolation_holds()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        await ManagedFieldNoLeak.AssertNoLeakAsync();
    }
}
