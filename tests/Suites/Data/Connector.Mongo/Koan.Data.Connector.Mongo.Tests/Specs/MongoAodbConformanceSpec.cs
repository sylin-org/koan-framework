using System.Collections.Generic;
using Koan.Data.AdapterSurface.TestKit;

namespace Koan.Data.Connector.Mongo.Tests.Specs;

/// <summary>
/// Mongo's AODB conformance ledger cell (ARCH-0103 §6 / P5) — the golden reference. Proves, through a real
/// <c>AddKoan()</c> boot over one Mongo container, that the golden <c>MongoDocumentStore</c> realizes all three AODB
/// isolation modes AND declares the matching tokens. The two routed conformance sources share the one Mongo server but
/// live in distinct physical <b>databases</b> (the placement <c>MongoAdapterFactory</c> pools by connection+database).
/// </summary>
public sealed class MongoAodbConformanceSpec(MongoFixture fixture, ITestOutputHelper output)
    : AodbConformanceSpecsBase<MongoFixture>(fixture, output)
{
    protected override IEnumerable<KeyValuePair<string, string?>> RoutedSourceSettings()
    {
        var conn = Fixture.ConnectionString;
        return new Dictionary<string, string?>
        {
            ["Koan:Data:Sources:conformance_a:Adapter"] = "mongo",
            ["Koan:Data:Sources:conformance_a:mongo:ConnectionString"] = conn,
            ["Koan:Data:Sources:conformance_a:mongo:Database"] = "koan_conf_a",
            ["Koan:Data:Sources:conformance_b:Adapter"] = "mongo",
            ["Koan:Data:Sources:conformance_b:mongo:ConnectionString"] = conn,
            ["Koan:Data:Sources:conformance_b:mongo:Database"] = "koan_conf_b",
        };
    }
}
