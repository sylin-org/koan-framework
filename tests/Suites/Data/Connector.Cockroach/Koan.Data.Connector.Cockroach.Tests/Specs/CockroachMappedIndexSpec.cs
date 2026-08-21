using Koan.Data.Abstractions.Annotations;
using Npgsql;

namespace Koan.Data.Connector.Cockroach.Tests.Specs;

/// <summary>
/// CockroachDB speaks the PostgreSQL wire and shares its dialect, but it is a different engine with its own
/// planner and its own rules about what may be indexed. Sharing a runtime is not evidence, so the declared index
/// is proven here on its own terms (PMC-041).
/// </summary>
public sealed class CockroachMappedIndexSpec(CockroachFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<CockroachFixture>(fixture, output)
{
    [Fact(DisplayName = "Cockroach: a declared index is built over the expression its reads emit")]
    public async Task Declared_indexes_are_built_and_usable()
    {
        RequireBackingStore();
        await using (var host = await BootAsync())
        {
            await new IndexedProbe
            {
                Id = "probe-1",
                Status = 1,
                DueAt = DateTimeOffset.UtcNow
            }.Save();
        }

        await using var verify = new NpgsqlConnection(Fixture.ConnectionString);
        await verify.OpenAsync(TestContext.Current.CancellationToken);

        await using (var definition = verify.CreateCommand())
        {
            definition.CommandText = "SELECT indexdef FROM pg_indexes WHERE indexname = @name";
            definition.Parameters.AddWithValue("name", "ix_cockroach_probe_status_due");
            var sql = (string?)await definition.ExecuteScalarAsync(TestContext.Current.CancellationToken);

            sql.Should().NotBeNull("the declared index must exist, not merely be planned");
            Output.WriteLine(sql);
            sql.Should().Contain("Json").And.Contain("Status").And.Contain("DueAt");
        }

        // Cost preference is a function of table size, and this table holds one row. Naming the index is how
        // this engine is asked the narrower question: can it serve the read at all? It refuses the statement
        // outright when the index cannot.
        await using var explain = verify.CreateCommand();
        explain.CommandText = """
            EXPLAIN
            SELECT "Id"
            FROM "KOAN_MAPPED_INDEX_PROBE"@ix_cockroach_probe_status_due
            WHERE (("Json" #>> '{Status}'))::bigint = 1
            """;
        var plan = new List<string>();
        await using (var reader = await explain.ExecuteReaderAsync(TestContext.Current.CancellationToken))
            while (await reader.ReadAsync(TestContext.Current.CancellationToken))
                plan.Add(string.Join(" | ", Enumerable.Range(0, reader.FieldCount)
                    .Select(column => reader.IsDBNull(column) ? "" : reader.GetValue(column).ToString())));

        Output.WriteLine(string.Join(Environment.NewLine, plan));
        plan.Should().Contain(line => line.Contains(
            "ix_cockroach_probe_status_due",
            StringComparison.OrdinalIgnoreCase));
    }

    [Storage(Name = "KOAN_MAPPED_INDEX_PROBE")]
    private sealed class IndexedProbe : Entity<IndexedProbe>
    {
        [Index(Name = "ix_cockroach_probe_status_due", Order = 0)]
        public int Status { get; set; }

        [Index(Name = "ix_cockroach_probe_status_due", Order = 1)]
        public DateTimeOffset DueAt { get; set; }
    }
}
