using Koan.Data.Abstractions.Annotations;
using Microsoft.Data.Sqlite;

namespace Koan.Data.Connector.Sqlite.Tests.Specs;

public sealed class SqliteMappedIndexSpec(SqliteFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<SqliteFixture>(fixture, output)
{
    [Fact]
    public async Task Managed_indexes_use_filter_expressions_and_repair_stale_definitions()
    {
        RequireBackingStore();
        var directConnectionString = new SqliteConnectionStringBuilder(Fixture.ConnectionString)
        {
            Pooling = false
        }.ToString();
        await using (var host = await BootAsync())
        {
            await new IndexedProbe
            {
                Id = "probe-1",
                Status = 1,
                DueAt = DateTimeOffset.UtcNow
            }.Save();
        }

        await using (var connection = new SqliteConnection(directConnectionString))
        {
            await connection.OpenAsync();
            await using var corrupt = connection.CreateCommand();
            corrupt.CommandText = """
                DROP INDEX "ix_sqlite_probe_status_due";
                CREATE INDEX "ix_sqlite_probe_status_due" ON "KOAN_MAPPED_INDEX_PROBE" ("Json");
                """;
            await corrupt.ExecuteNonQueryAsync();
        }

        await using (var host = await BootAsync())
            _ = await IndexedProbe.Get("probe-1");

        await using var verify = new SqliteConnection(directConnectionString);
        await verify.OpenAsync();
        await using (var definition = verify.CreateCommand())
        {
            definition.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'index' AND name = @name";
            definition.Parameters.AddWithValue("@name", "ix_sqlite_probe_status_due");
            var sql = (string?)await definition.ExecuteScalarAsync();
            sql.Should().Contain("json_extract(\"Json\", '$.\"Status\"')");
            sql.Should().Contain("json_extract(\"Json\", '$.\"DueAt\"')");
        }

        await using var explain = verify.CreateCommand();
        explain.CommandText = """
            EXPLAIN QUERY PLAN
            SELECT "Id"
            FROM "KOAN_MAPPED_INDEX_PROBE" AS koan_row
            WHERE CAST(json_extract(koan_row."Json", '$."Status"') AS NUMERIC) = 1
            ORDER BY json_extract(koan_row."Json", '$."DueAt"')
            LIMIT 1
            """;
        var details = new List<string>();
        await using (var reader = await explain.ExecuteReaderAsync())
            while (await reader.ReadAsync()) details.Add(reader.GetString(3));
        details.Should().Contain(detail => detail.Contains(
            "USING INDEX ix_sqlite_probe_status_due",
            StringComparison.OrdinalIgnoreCase));
    }

    [Storage(Name = "KOAN_MAPPED_INDEX_PROBE")]
    private sealed class IndexedProbe : Entity<IndexedProbe>
    {
        [Index(Name = "ix_sqlite_probe_status_due", Order = 0)]
        public int Status { get; set; }

        [Index(Name = "ix_sqlite_probe_status_due", Order = 1)]
        public DateTimeOffset DueAt { get; set; }
    }
}
