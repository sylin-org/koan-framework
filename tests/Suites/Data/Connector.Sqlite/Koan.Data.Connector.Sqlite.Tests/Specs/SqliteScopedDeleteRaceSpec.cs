using Koan.Data.AdapterSurface.TestKit;

namespace Koan.Data.Connector.Sqlite.Tests.Specs;

/// <summary>
/// DATA-0156 on the real Sqlite adapter: a scoped delete carries the scope into the store's own delete, so a row that
/// leaves the scope between the facade's check and the store's act is not removed (<see cref="ScopedDeleteRace"/>).
/// </summary>
public sealed class SqliteScopedDeleteRaceSpec(SqliteFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<SqliteFixture>(fixture, output)
{
    [Fact(DisplayName = "Sqlite: a scoped single, many and batch delete removes nothing once the row left the scope (NK-168, NK-181, NK-303)")]
    public async Task Scoped_deletes_are_atomic_at_the_store()
    {
        RequireBackingStore();
        await using var host = await BootAsync(ScopedDeleteRace.Declare, ScopedDeleteRace.Configure);
        ScopedDeleteRace.AssertDeclared(host.Services, atStore: true);
        await ScopedDeleteRace.AssertAtomicAsync();
    }
}
