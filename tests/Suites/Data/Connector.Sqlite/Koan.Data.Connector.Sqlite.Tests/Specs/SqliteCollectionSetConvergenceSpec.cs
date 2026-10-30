using Koan.Data.AdapterSurface.TestKit;

namespace Koan.Data.Connector.Sqlite.Tests.Specs;

/// <summary>
/// DATA-0162 on the real Sqlite adapter: HasAny, HasAll and HasNone over every element type answer what their per-member
/// form answers, at every size, executed by the store (<see cref="CollectionSetConvergence"/>).
/// </summary>
public sealed class SqliteCollectionSetConvergenceSpec(SqliteFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<SqliteFixture>(fixture, output)
{
    [Fact(DisplayName = "Sqlite: list filters over JSON arrays answer what their per-member form answers")]
    public async Task List_filters_converge()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        using var _ = Lease(NewPartition("lists"));
        foreach (var note in await CollectionSetConvergence.AssertConvergesAsync(host.Services)) Output.WriteLine(note);
    }
}
