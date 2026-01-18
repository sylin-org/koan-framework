using System;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Koan.Data.Connector.PGVector;

/// <summary>
/// Builds and manages pgvector indexes: HNSW and IVFFlat.
/// Provides index creation, maintenance, and performance tuning.
/// </summary>
public sealed class PgVectorIndexBuilder
{
    private readonly NpgsqlConnection _conn;
    private readonly PGVectorOptions _options;
    private readonly ILogger? _logger;

    public PgVectorIndexBuilder(
        NpgsqlConnection conn,
        PGVectorOptions options,
        ILogger? logger = null)
    {
        _conn = conn ?? throw new ArgumentNullException(nameof(conn));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;
    }

    /// <summary>
    /// Creates HNSW (Hierarchical Navigable Small World) index.
    /// Best for: Production search with high recall requirements.
    /// Trade-offs: Excellent query performance, slower build, higher memory.
    /// </summary>
    /// <param name="tableName">Vector table name (auto-includes "_vector" suffix per DATA-0087)</param>
    /// <param name="m">Maximum number of connections per layer (16=balanced, 32=high quality)</param>
    /// <param name="efConstruction">Size of dynamic candidate list during construction (64=balanced, 128=high quality)</param>
    /// <param name="ct">Cancellation token</param>
    public async Task CreateHnswIndexAsync(
        string tableName,
        int m = 16,
        int efConstruction = 64,
        CancellationToken ct = default)
    {
        using var activity = PGVectorTelemetry.Activity.StartActivity("vector.createHnswIndex");

        var indexName = $"{tableName}_embedding_hnsw_idx";
        var distanceOps = GetDistanceOps();

        var sql = $@"
            CREATE INDEX {indexName}
            ON {tableName}
            USING hnsw (embedding {distanceOps})
            WITH (m = {m}, ef_construction = {efConstruction})";

        _logger?.LogInformation(
            "Creating HNSW index on {TableName} (m={M}, ef_construction={EfConstruction}, metric={Metric})",
            tableName,
            m,
            efConstruction,
            _options.DistanceMetric);

        await _conn.ExecuteAsync(sql, commandTimeout: 600); // 10 minute timeout for large datasets

        _logger?.LogInformation("Created HNSW index {IndexName}", indexName);
    }

    /// <summary>
    /// Creates IVFFlat (Inverted File with Flat compression) index.
    /// Best for: Large datasets where build time and memory matter.
    /// Trade-offs: Fast build, lower memory, slightly lower recall than HNSW.
    /// </summary>
    /// <param name="tableName">Vector table name (auto-includes "_vector" suffix per DATA-0087)</param>
    /// <param name="lists">Number of inverted lists/clusters (sqrt(rows) recommended)</param>
    /// <param name="ct">Cancellation token</param>
    public async Task CreateIvfflatIndexAsync(
        string tableName,
        int lists = 100,
        CancellationToken ct = default)
    {
        using var activity = PGVectorTelemetry.Activity.StartActivity("vector.createIvfflatIndex");

        var indexName = $"{tableName}_embedding_ivfflat_idx";
        var distanceOps = GetDistanceOps();

        var sql = $@"
            CREATE INDEX {indexName}
            ON {tableName}
            USING ivfflat (embedding {distanceOps})
            WITH (lists = {lists})";

        _logger?.LogInformation(
            "Creating IVFFlat index on {TableName} (lists={Lists}, metric={Metric})",
            tableName,
            lists,
            _options.DistanceMetric);

        await _conn.ExecuteAsync(sql, commandTimeout: 600); // 10 minute timeout for large datasets

        _logger?.LogInformation("Created IVFFlat index {IndexName}", indexName);
    }

    /// <summary>
    /// Drops an existing index.
    /// Useful before bulk loading or when switching index types.
    /// </summary>
    public async Task DropIndexAsync(string indexName, CancellationToken ct = default)
    {
        using var activity = PGVectorTelemetry.Activity.StartActivity("vector.dropIndex");

        var sql = $"DROP INDEX IF EXISTS {indexName}";

        await _conn.ExecuteAsync(sql);

        _logger?.LogInformation("Dropped index {IndexName}", indexName);
    }

    /// <summary>
    /// Rebuilds an existing index (REINDEX).
    /// Useful after bulk updates or when index becomes fragmented.
    /// </summary>
    public async Task ReindexAsync(string indexName, CancellationToken ct = default)
    {
        using var activity = PGVectorTelemetry.Activity.StartActivity("vector.reindex");

        var sql = $"REINDEX INDEX {indexName}";

        _logger?.LogInformation("Reindexing {IndexName}", indexName);

        await _conn.ExecuteAsync(sql, commandTimeout: 600);

        _logger?.LogInformation("Reindexed {IndexName}", indexName);
    }

    /// <summary>
    /// Gets index statistics: size, rows indexed, usage.
    /// </summary>
    public async Task<IndexStats> GetIndexStatsAsync(string indexName)
    {
        var sql = @"
            SELECT
                pg_size_pretty(pg_relation_size(@indexName::regclass)) as size,
                idx_scan as scans,
                idx_tup_read as tuples_read,
                idx_tup_fetch as tuples_fetched
            FROM pg_stat_user_indexes
            WHERE indexrelname = @indexName";

        var stats = await _conn.QuerySingleOrDefaultAsync<IndexStats>(sql, new { indexName });

        return stats ?? new IndexStats();
    }

    /// <summary>
    /// Analyzes table to update statistics.
    /// Run after bulk loading for optimal query planning.
    /// </summary>
    public async Task AnalyzeTableAsync(string tableName, CancellationToken ct = default)
    {
        using var activity = PGVectorTelemetry.Activity.StartActivity("vector.analyzeTable");

        var sql = $"ANALYZE {tableName}";

        await _conn.ExecuteAsync(sql);

        _logger?.LogDebug("Analyzed table {TableName}", tableName);
    }

    /// <summary>
    /// Vacuums table to reclaim storage and update statistics.
    /// Run after bulk deletes.
    /// </summary>
    public async Task VacuumTableAsync(string tableName, CancellationToken ct = default)
    {
        using var activity = PGVectorTelemetry.Activity.StartActivity("vector.vacuumTable");

        var sql = $"VACUUM ANALYZE {tableName}";

        await _conn.ExecuteAsync(sql);

        _logger?.LogInformation("Vacuumed table {TableName}", tableName);
    }

    private string GetDistanceOps()
    {
        return _options.DistanceMetric switch
        {
            DistanceMetric.Cosine => "vector_cosine_ops",
            DistanceMetric.L2 => "vector_l2_ops",
            DistanceMetric.InnerProduct => "vector_ip_ops",
            _ => "vector_cosine_ops"
        };
    }
}

/// <summary>
/// Index statistics from pg_stat_user_indexes.
/// </summary>
public record IndexStats
{
    public string Size { get; init; } = "0 bytes";
    public long Scans { get; init; }
    public long TuplesRead { get; init; }
    public long TuplesFetched { get; init; }
}
