using System;
using System.Collections.Generic;
using Koan.Data.AdapterSurface.TestKit;

namespace Koan.Data.Connector.Couchbase.Tests.Specs;

/// <summary>
/// Couchbase's AODB conformance ledger cell (ARCH-0103 §6 / P4) — the last document-family adapter to fold onto
/// <c>DocumentStore</c>. Proves, through a real <c>AddKoan()</c> boot over one Couchbase container, that the folded
/// <c>CouchbaseDocumentStore</c> realizes all three AODB isolation modes AND declares the matching tokens. Couchbase's
/// 3-level keyspace maps each mode to a distinct native level: Shared (the framework-managed discriminator injected into
/// the document JSON + a CAS conflict guard), Container (a distinct native <b>scope</b> per ambient partition), Database
/// (a distinct native <b>bucket</b> per routed source). The two routed conformance sources point to freshly-provisioned
/// buckets on the fixture's cluster.
/// </summary>
public sealed class CouchbaseAodbConformanceSpec(CouchbaseFixture fixture, ITestOutputHelper output)
    : AodbConformanceSpecsBase<CouchbaseFixture>(fixture, output)
{
    protected override IEnumerable<KeyValuePair<string, string?>> RoutedSourceSettings()
    {
        var conn = Fixture.ConnectionString;
        var bucketA = ProvisionBucket("a");
        var bucketB = ProvisionBucket("b");
        return new Dictionary<string, string?>
        {
            ["Koan:Data:Sources:conformance_a:Adapter"] = "couchbase",
            ["Koan:Data:Sources:conformance_a:couchbase:ConnectionString"] = conn,
            ["Koan:Data:Sources:conformance_a:couchbase:Bucket"] = bucketA,
            ["Koan:Data:Sources:conformance_a:couchbase:Username"] = Fixture.AdminUser,
            ["Koan:Data:Sources:conformance_a:couchbase:Password"] = Fixture.AdminPassword,
            ["Koan:Data:Sources:conformance_a:couchbase:ManagementUrl"] = Fixture.ManagementUrl,
            ["Koan:Data:Sources:conformance_b:Adapter"] = "couchbase",
            ["Koan:Data:Sources:conformance_b:couchbase:ConnectionString"] = conn,
            ["Koan:Data:Sources:conformance_b:couchbase:Bucket"] = bucketB,
            ["Koan:Data:Sources:conformance_b:couchbase:Username"] = Fixture.AdminUser,
            ["Koan:Data:Sources:conformance_b:couchbase:Password"] = Fixture.AdminPassword,
            ["Koan:Data:Sources:conformance_b:couchbase:ManagementUrl"] = Fixture.ManagementUrl,
        };
    }

    /// <summary>Provision a fresh per-source bucket on the fixture's cluster (Database mode = a distinct native bucket).
    /// A unique per-run name keeps each Database-cell run clean; the buckets are reclaimed with the container.</summary>
    private string ProvisionBucket(string slot)
    {
        var name = "koan_aodb_" + slot + "_" + Guid.CreateVersion7().ToString("n")[..8];
        Fixture.ProvisionBucketAsync(name).GetAwaiter().GetResult();
        return name;
    }
}
