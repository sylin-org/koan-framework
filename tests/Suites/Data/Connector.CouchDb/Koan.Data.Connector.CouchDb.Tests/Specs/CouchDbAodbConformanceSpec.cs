using Koan.Data.AdapterSurface.TestKit;

namespace Koan.Data.Connector.CouchDb.Tests.Specs;

/// <summary>
/// CouchDB's AODB conformance suite: the record-plane oracle against a real CouchDB 3.5 container.
/// The two routed conformance sources resolve to distinct database-name prefixes on the same server —
/// this store's container is a database, so the prefix is the placement.
/// </summary>
public sealed class CouchDbAodbConformanceSpec(CouchDbFixture fixture, ITestOutputHelper output)
    : AodbConformanceSpecsBase<CouchDbFixture>(fixture, output)
{
    protected override IEnumerable<KeyValuePair<string, string?>> RoutedSourceSettings() =>
        new Dictionary<string, string?>
        {
            ["Koan:Data:Sources:conformance_a:Adapter"] = "couchdb",
            ["Koan:Data:Sources:conformance_a:ConnectionString"] = Fixture.ConnectionString,
            ["Koan:Data:Sources:conformance_a:CouchDb:Database"] = Prefix("a"),
            ["Koan:Data:Sources:conformance_b:Adapter"] = "couchdb",
            ["Koan:Data:Sources:conformance_b:ConnectionString"] = Fixture.ConnectionString,
            ["Koan:Data:Sources:conformance_b:CouchDb:Database"] = Prefix("b"),
        };

    private static string Prefix(string slot) => $"koan_aodb_conf_{slot}_{Guid.CreateVersion7():N}";
}
