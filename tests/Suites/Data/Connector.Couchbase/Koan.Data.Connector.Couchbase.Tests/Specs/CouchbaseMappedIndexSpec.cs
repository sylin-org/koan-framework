using Couchbase;
using Couchbase.Core.IO.Authentication.Authenticators;
using Couchbase.Query;
using Koan.Data.Abstractions.Annotations;
using Newtonsoft.Json.Linq;

namespace Koan.Data.Connector.Couchbase.Tests.Specs;

/// <summary>
/// A primary index makes a keyspace queryable; it does not make a query fast. Every predicate Koan pushes down
/// reads a path inside the document, and without a matching GSI the query service scans the primary index for
/// all of them — so a declared <c>[Index]</c> that built nothing was invisible in results and expensive in
/// practice (PMC-041).
/// </summary>
public sealed class CouchbaseMappedIndexSpec(CouchbaseFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<CouchbaseFixture>(fixture, output)
{
    [Fact(DisplayName = "Couchbase: a declared index becomes a secondary GSI the query service uses")]
    public async Task Declared_indexes_are_built_and_usable()
    {
        RequireBackingStore();
        await using (var host = await BootAsync())
        {
            await new IndexedProbe { Id = "probe-1", Status = 1, DueAt = DateTimeOffset.UtcNow }.Save();

            // This store provisions its query indexes when a keyspace is first queried rather than first
            // written, so the read is what exercises the path an application actually takes.
            (await IndexedProbe.Query(probe => probe.Status == 1)).Should().ContainSingle();
        }

        var options = new ClusterOptions { ConnectionString = Fixture.ConnectionString }
            .WithAuthenticator(new PasswordAuthenticator(Fixture.AdminUser, Fixture.AdminPassword));
        using var cluster = await Cluster.ConnectAsync(options);

        var declared = await Rows(cluster, """
            SELECT RAW {"bucket": bucket_id, "scope": scope_id, "keyspace": keyspace_id}
              FROM system:indexes
             WHERE name = 'ix_couchbase_probe_status_due'
            """);
        declared.Should().ContainSingle("the declared index must exist, not merely be planned");

        var located = JObject.Parse(declared[0]);
        var keyspace = $"`{located["bucket"]}`.`{located["scope"]}`.`{located["keyspace"]}`";

        // The claim is that the query service can serve the read from this index rather than scanning the
        // primary one. EXPLAIN names the index it chose.
        var plan = await Rows(cluster, $"EXPLAIN SELECT RAW doc FROM {keyspace} AS doc WHERE doc.`status` = 1");
        var text = string.Join(Environment.NewLine, plan);
        Output.WriteLine(text);
        text.Should().Contain("ix_couchbase_probe_status_due");
    }

    private static async Task<List<string>> Rows(ICluster cluster, string statement)
    {
        var rows = new List<string>();
        var result = await cluster.QueryAsync<dynamic>(
            statement,
            new QueryOptions().Readonly(true).Timeout(TimeSpan.FromSeconds(30)));
        await foreach (var row in result.Rows) rows.Add(row?.ToString() ?? string.Empty);
        return rows;
    }

    private sealed class IndexedProbe : Entity<IndexedProbe>
    {
        [Index(Name = "ix_couchbase_probe_status_due", Order = 0)]
        public int Status { get; set; }

        [Index(Name = "ix_couchbase_probe_status_due", Order = 1)]
        public DateTimeOffset DueAt { get; set; }
    }
}
