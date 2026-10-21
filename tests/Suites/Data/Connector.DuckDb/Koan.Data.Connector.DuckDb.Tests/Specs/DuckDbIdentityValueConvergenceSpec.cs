using Koan.Data.AdapterSurface.TestKit;

namespace Koan.Data.Connector.DuckDb.Tests.Specs;

/// <summary>Guid and DateTime document filters converge with the CLR oracle (NK-052).</summary>
public sealed class DuckDbIdentityValueConvergenceSpec(DuckDbFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<DuckDbFixture>(fixture, output)
{
    [Fact(DisplayName = "DuckDb: Guid and DateTime filters converge with the CLR oracle")]
    public async Task Guid_and_DateTime_filters_converge()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        await IdentityValueConvergence.AssertConvergesAsync();
    }
}
