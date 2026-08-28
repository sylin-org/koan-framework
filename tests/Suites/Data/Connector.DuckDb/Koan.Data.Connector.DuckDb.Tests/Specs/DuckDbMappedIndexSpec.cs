using Koan.Data.Abstractions.Annotations;
using DuckDB.NET.Data;

namespace Koan.Data.Connector.DuckDb.Tests.Specs;

/// <summary>
/// Mapped indexes over a JSON document column are expression indexes, and their value depends on the
/// planner choosing them for the query's exact read expression. SQLite provably matches those; DuckDB's
/// planner matching is unproven, so <c>DuckDbStoreFeatures.SupportsRewriteFreeExpressionIndexes</c> is
/// deliberately false and the orchestrator DECLINES to build them — the honest envelope, stated at
/// startup rather than shipped as an index the engine may ignore. This spec pins the decline: boot
/// succeeds, the entity stays fully usable through scans, and no document-expression index appears.
/// Plain-column mapped indexes (a future non-document storage shape) remain governed by
/// <c>SupportsMappedIndexes</c>.
/// </summary>
public sealed class DuckDbMappedIndexSpec(DuckDbFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<DuckDbFixture>(fixture, output)
{
    [Fact]
    public async Task Document_expression_mapped_indexes_are_declined_not_silently_misbuilt()
    {
        RequireBackingStore();
        await using (var host = await BootAsync())
        {
            var saved = await new IndexedProbe
            {
                Id = "probe-1",
                Status = 1,
                DueAt = DateTimeOffset.UtcNow
            }.Save();
            (await IndexedProbe.Get(saved.Id)).Should().NotBeNull(
                "the decline is about indexes, never about ordinary Entity CRUD");
        }

        await using (var host = await BootAsync())
        {
            // A second boot re-runs the schema pass; the decline must be stable, not a first-boot accident.
            _ = await IndexedProbe.Query(p => p.Status == 1);
        }

        await using var verify = new DuckDBConnection(Fixture.ConnectionString);
        await verify.OpenAsync();
        await using var indexes = verify.CreateCommand();
        indexes.CommandText = "SELECT COUNT(*) FROM duckdb_indexes() WHERE table_name = 'KOAN_MAPPED_INDEX_PROBE'";
        Convert.ToInt64(await indexes.ExecuteScalarAsync()).Should().Be(0,
            "the orchestrator must not build expression indexes it cannot prove the planner uses");
    }

    [Storage(Name = "KOAN_MAPPED_INDEX_PROBE")]
    private sealed class IndexedProbe : Entity<IndexedProbe>
    {
        [Index(Name = "ix_duckdb_probe_status_due", Order = 0)]
        public int Status { get; set; }

        [Index(Name = "ix_duckdb_probe_status_due", Order = 1)]
        public DateTimeOffset DueAt { get; set; }
    }
}
