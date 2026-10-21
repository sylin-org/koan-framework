using Koan.Data.AdapterSurface.TestKit;

namespace Koan.Data.Connector.Json.Tests.Specs.Isolation;

/// <summary>
/// DATA-0156: Json has no conditional delete, so a scoped delete stays a facade check-then-delete. The gap is declared
/// rather than hidden: the axis report says the delete is not carried to the store.
/// </summary>
public sealed class JsonScopedDeleteGapSpec(JsonFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<JsonFixture>(fixture, output)
{
    [Fact(DisplayName = "Json: the axis report declares that scoped deletes are checked first, not carried to the store")]
    public async Task Scoped_delete_gap_is_declared()
    {
        RequireBackingStore();
        await using var host = await BootAsync(ScopedDeleteRace.Declare, ScopedDeleteRace.Configure);
        ScopedDeleteRace.AssertDeclared(host.Services, atStore: false);
    }
}
