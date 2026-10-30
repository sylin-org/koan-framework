using Koan.Data.AdapterSurface.TestKit;

namespace Koan.Data.Connector.Postgres.Tests.Specs.Filtering;

/// <summary>
/// DATA-0162 on the real Postgres adapter: In and Nin over every scalar type answer what Eq answers member by member, on document
/// fields and typed columns, at every size and with two sets in one statement, executed by the store (<see cref="SetMembershipConvergence"/>).
/// </summary>
public sealed class PostgresSetMembershipConvergenceSpec(PostgresFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<PostgresFixture>(fixture, output)
{
    [Fact(DisplayName = "Postgres: set filters answer what Eq answers at every size")]
    public async Task Set_filters_converge()
    {
        RequireBackingStore();
        // A timestamptz column holds no offset, and an offset read back from it does not hydrate, so the offset
        // property is checked on the document only here.
        await using var host = await BootAsync(SetMembershipConvergence.Declaration(offsetColumns: false, clockColumns: true));
        await SetMembershipConvergence.AssertConvergesAsync(host.Services, offsetColumns: false, clockColumns: true);
    }
}
