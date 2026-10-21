using Koan.Data.AdapterSurface.TestKit;

namespace Koan.Data.Connector.Postgres.Tests.Specs.Filtering;

/// <summary>Guid and DateTime document filters converge with the CLR oracle (NK-052).</summary>
public sealed class PostgresIdentityValueConvergenceSpec(PostgresFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<PostgresFixture>(fixture, output)
{
    [Fact(DisplayName = "Postgres: Guid and DateTime filters converge with the CLR oracle")]
    public async Task Guid_and_DateTime_filters_converge()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        await IdentityValueConvergence.AssertConvergesAsync();
    }
}
