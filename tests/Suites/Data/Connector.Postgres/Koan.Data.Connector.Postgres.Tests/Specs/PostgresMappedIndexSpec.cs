using Koan.Data.Abstractions.Annotations;
using Npgsql;

namespace Koan.Data.Connector.Postgres.Tests.Specs;

/// <summary>
/// A declared <c>[Index]</c> has to become an index the planner will actually choose. PostgreSQL uses an
/// expression index only when the query spells the value exactly as the index does, so the index is built from
/// the dialect's own read and this spec pins that spelling from the outside (PMC-041).
/// </summary>
public sealed class PostgresMappedIndexSpec(PostgresFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<PostgresFixture>(fixture, output)
{
    [Fact(DisplayName = "Postgres: a declared index is built over the expression its reads emit")]
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
            definition.Parameters.AddWithValue("name", "ix_postgres_probe_status_due");
            var sql = (string?)await definition.ExecuteScalarAsync(TestContext.Current.CancellationToken);
            sql.Should().NotBeNull("the declared index must exist, not merely be planned");
            sql.Should().Contain("Json").And.Contain("Status").And.Contain("DueAt");
        }

        // Cost preference is a function of table size, and this table holds one row. What the spec claims is
        // narrower and is the thing that was broken: the planner *can* satisfy these reads from the index.
        await using var transaction = await verify.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await using (var disable = verify.CreateCommand())
        {
            disable.Transaction = transaction;
            disable.CommandText = "SET LOCAL enable_seqscan = off";
            await disable.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        await using var explain = verify.CreateCommand();
        explain.Transaction = transaction;
        explain.CommandText = """
            EXPLAIN
            SELECT "Id"
            FROM "KOAN_MAPPED_INDEX_PROBE"
            WHERE (("Json" #>> '{Status}'))::bigint = 1
            ORDER BY ("Json" #>> '{DueAt}')
            LIMIT 1
            """;
        var plan = new List<string>();
        await using (var reader = await explain.ExecuteReaderAsync(TestContext.Current.CancellationToken))
            while (await reader.ReadAsync(TestContext.Current.CancellationToken))
                plan.Add(reader.GetString(0));

        Output.WriteLine(string.Join(Environment.NewLine, plan));
        plan.Should().Contain(line => line.Contains(
            "ix_postgres_probe_status_due",
            StringComparison.OrdinalIgnoreCase));
    }

    [Storage(Name = "KOAN_MAPPED_INDEX_PROBE")]
    private sealed class IndexedProbe : Entity<IndexedProbe>
    {
        [Index(Name = "ix_postgres_probe_status_due", Order = 0)]
        public int Status { get; set; }

        [Index(Name = "ix_postgres_probe_status_due", Order = 1)]
        public DateTimeOffset DueAt { get; set; }
    }
}
