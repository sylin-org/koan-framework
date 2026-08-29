using FirebirdSql.Data.FirebirdClient;
using Koan.Data.AdapterSurface.TestKit;

namespace Koan.Data.Connector.Firebird.Tests.Specs;

/// <summary>
/// Firebird's AODB conformance suite (ARCH-0103 §6 / P5): the record-plane oracle, hosted against a real
/// Firebird 5 container. The two routed conformance sources resolve to distinct database files on the
/// same server, created here over the wire — the same provision-then-route shape the MySQL sibling uses.
/// </summary>
public sealed class FirebirdAodbConformanceSpec(FirebirdFixture fixture, ITestOutputHelper output)
    : AodbConformanceSpecsBase<FirebirdFixture>(fixture, output)
{
    protected override IEnumerable<KeyValuePair<string, string?>> RoutedSourceSettings() =>
        new Dictionary<string, string?>
        {
            ["Koan:Data:Sources:conformance_a:Adapter"] = "firebird",
            ["Koan:Data:Sources:conformance_a:ConnectionString"] = ProvisionDatabase("a"),
            ["Koan:Data:Sources:conformance_b:Adapter"] = "firebird",
            ["Koan:Data:Sources:conformance_b:ConnectionString"] = ProvisionDatabase("b"),
        };

    private string ProvisionDatabase(string slot)
    {
        var database = $"/var/lib/firebird/data/koan_conf_{slot}_{Guid.CreateVersion7():N}.fdb";
        var builder = new FbConnectionStringBuilder(Fixture.ConnectionString) { Database = database };
        FbConnection.CreateDatabase(builder.ConnectionString, pageSize: 16384, forcedWrites: true, overwrite: false);
        return builder.ConnectionString;
    }
}
