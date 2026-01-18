using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Npgsql;
using Pgvector;
using Koan.Data.Abstractions;
using Koan.Data.Vector.Abstractions;
using Koan.Data.Vector.Abstractions.Configuration;

namespace Koan.Data.Connector.PGVector;

/// <summary>
/// PostgreSQL vector repository using pgvector extension.
/// Implements IVectorSearchRepository with support for cosine/L2/inner product similarity,
/// HNSW and IVFFlat indexes, and bulk operations.
/// Table names automatically include "_vector" suffix per DATA-0087.
/// </summary>
internal sealed class PGVectorRepository<TEntity, TKey>
    : IVectorSearchRepository<TEntity, TKey>, IVectorCapabilities
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly IServiceProvider _sp;
    private readonly PGVectorOptions _options;
    private readonly ILogger<PGVectorRepository<TEntity, TKey>>? _logger;
    private readonly PgVectorExtensionManager _extensionManager;
    private bool _schemaEnsured;

    public PGVectorRepository(
        NpgsqlDataSource dataSource,
        IServiceProvider sp,
        IOptions<PGVectorOptions> options,
        PgVectorExtensionManager extensionManager,
        ILogger<PGVectorRepository<TEntity, TKey>>? logger = null)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _sp = sp ?? throw new ArgumentNullException(nameof(sp));
        _options = options.Value ?? throw new ArgumentNullException(nameof(options));
        _extensionManager = extensionManager ?? throw new ArgumentNullException(nameof(extensionManager));
        _logger = logger;
    }

    public VectorCapabilities Capabilities =>
        VectorCapabilities.Knn |
        VectorCapabilities.Filters |
        VectorCapabilities.BulkUpsert |
        VectorCapabilities.BulkDelete |
        VectorCapabilities.DynamicCollections;

    /// <summary>
    /// Table name with automatic "_vector" suffix per DATA-0087.
    /// Prevents collisions with entity tables (e.g., "media_vector" vs "media").
    /// Includes partition if EntityContext.Current?.Partition is set.
    /// </summary>
    private string TableName
    {
        get
        {
            // Uses VectorStorageNameRegistry → automatic "_vector" suffix
            var name = VectorStorageNameRegistry.GetOrCompute<TEntity, TKey>(_sp);

            // Convert to lowercase and sanitize for PostgreSQL
            // Example: "Media_vector#partition1" → "media_vector#partition1"
            return name.ToLowerInvariant();
        }
    }

    /// <summary>
    /// Distance operator based on configured metric.
    /// </summary>
    private string DistanceOperator => _options.DistanceMetric switch
    {
        DistanceMetric.Cosine => "<=>",      // Cosine distance
        DistanceMetric.L2 => "<->",          // Euclidean (L2) distance
        DistanceMetric.InnerProduct => "<#>", // Negative inner product
        _ => "<=>"
    };

    public async Task VectorEnsureCreatedAsync(CancellationToken ct = default)
    {
        if (_schemaEnsured) return;

        using var activity = PGVectorTelemetry.Activity.StartActivity("vector.ensureCreated");
        var tableName = TableName;

        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        // Ensure pgvector extension exists
        await _extensionManager.EnsureExtensionAsync(conn, ct);

        // Check if table already exists
        var exists = await conn.ExecuteScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM pg_tables WHERE schemaname = 'public' AND tablename = @name)",
            new { name = tableName });

        if (exists)
        {
            _schemaEnsured = true;
            _logger?.LogDebug("PGVector table {TableName} already exists", tableName);
            return;
        }

        // Validate dimension
        if (!_extensionManager.ValidateDimension(_options.DefaultDimension))
        {
            throw new InvalidOperationException(
                $"Invalid dimension {_options.DefaultDimension}. " +
                $"Must be between 1 and {PGVectorOptions.MaxDimension}.");
        }

        // Create vector table
        var createTableSql = $@"
            CREATE TABLE {tableName} (
                id TEXT PRIMARY KEY,
                embedding vector({_options.DefaultDimension}),
                metadata JSONB,
                created_at TIMESTAMPTZ DEFAULT NOW(),
                updated_at TIMESTAMPTZ DEFAULT NOW()
            )";

        await conn.ExecuteAsync(createTableSql);

        _logger?.LogInformation(
            "Created PGVector table {TableName} with dimension {Dimension}",
            tableName,
            _options.DefaultDimension);

        // Create default index if configured
        if (_options.AutoCreateIndex && _options.DefaultIndexType != IndexType.None)
        {
            var indexBuilder = new PgVectorIndexBuilder(conn, _options, _logger);

            switch (_options.DefaultIndexType)
            {
                case IndexType.Hnsw:
                    await indexBuilder.CreateHnswIndexAsync(
                        tableName,
                        m: _options.HnswM,
                        efConstruction: _options.HnswEfConstruction,
                        ct: ct);
                    break;

                case IndexType.IvfFlat:
                    await indexBuilder.CreateIvfflatIndexAsync(
                        tableName,
                        lists: _options.IvfFlatLists,
                        ct: ct);
                    break;
            }
        }

        _schemaEnsured = true;
    }

    public async Task UpsertAsync(
        TKey id,
        float[] embedding,
        object? metadata = null,
        CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);

        using var activity = PGVectorTelemetry.Activity.StartActivity("vector.upsert");
        var tableName = TableName;

        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        var metadataJson = metadata != null
            ? JsonConvert.SerializeObject(metadata)
            : null;

        // Use Pgvector.Vector for proper serialization
        var vector = new Vector(embedding);

        var sql = $@"
            INSERT INTO {tableName} (id, embedding, metadata, updated_at)
            VALUES (@id, @embedding, @metadata::jsonb, NOW())
            ON CONFLICT (id) DO UPDATE
            SET embedding = EXCLUDED.embedding,
                metadata = EXCLUDED.metadata,
                updated_at = NOW()";

        await conn.ExecuteAsync(sql, new
        {
            id = id.ToString(),
            embedding = vector,
            metadata = metadataJson
        });

        _logger?.LogTrace("Upserted vector {Id} to {TableName}", id, tableName);
    }

    public async Task<int> UpsertManyAsync(
        IEnumerable<(TKey Id, float[] Embedding, object? Metadata)> items,
        CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);

        using var activity = PGVectorTelemetry.Activity.StartActivity("vector.upsertMany");
        var tableName = TableName;
        var itemsList = items.ToList();

        if (itemsList.Count == 0)
            return 0;

        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        // Use batch insert with VALUES for better performance
        var batchSize = 100; // Postgres parameter limit considerations
        var totalInserted = 0;

        for (int i = 0; i < itemsList.Count; i += batchSize)
        {
            var batch = itemsList.Skip(i).Take(batchSize).ToList();
            var values = new StringBuilder();
            var parameters = new DynamicParameters();

            for (int j = 0; j < batch.Count; j++)
            {
                var item = batch[j];
                if (j > 0) values.Append(", ");

                values.Append($"(@id{j}, @embedding{j}, @metadata{j}::jsonb, NOW())");

                parameters.Add($"id{j}", item.Id.ToString());
                parameters.Add($"embedding{j}", new Vector(item.Embedding));
                parameters.Add($"metadata{j}",
                    item.Metadata != null ? JsonConvert.SerializeObject(item.Metadata) : null);
            }

            var sql = $@"
                INSERT INTO {tableName} (id, embedding, metadata, updated_at)
                VALUES {values}
                ON CONFLICT (id) DO UPDATE
                SET embedding = EXCLUDED.embedding,
                    metadata = EXCLUDED.metadata,
                    updated_at = NOW()";

            await conn.ExecuteAsync(sql, parameters);
            totalInserted += batch.Count;
        }

        _logger?.LogDebug("Upserted {Count} vectors to {TableName}", totalInserted, tableName);

        return totalInserted;
    }

    public async Task<bool> DeleteAsync(TKey id, CancellationToken ct = default)
    {
        using var activity = PGVectorTelemetry.Activity.StartActivity("vector.delete");
        var tableName = TableName;

        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        var sql = $"DELETE FROM {tableName} WHERE id = @id";
        var affected = await conn.ExecuteAsync(sql, new { id = id.ToString() });

        _logger?.LogTrace("Deleted vector {Id} from {TableName} (affected: {Affected})", id, tableName, affected);

        return affected > 0;
    }

    public async Task<int> DeleteManyAsync(IEnumerable<TKey> ids, CancellationToken ct = default)
    {
        using var activity = PGVectorTelemetry.Activity.StartActivity("vector.deleteMany");
        var tableName = TableName;
        var idsList = ids.Select(x => x.ToString()).ToArray();

        if (idsList.Length == 0)
            return 0;

        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        var sql = $"DELETE FROM {tableName} WHERE id = ANY(@ids)";
        var affected = await conn.ExecuteAsync(sql, new { ids = idsList });

        _logger?.LogDebug("Deleted {Count} vectors from {TableName}", affected, tableName);

        return affected;
    }

    public async Task<float[]?> GetEmbeddingAsync(TKey id, CancellationToken ct = default)
    {
        using var activity = PGVectorTelemetry.Activity.StartActivity("vector.getEmbedding");
        var tableName = TableName;

        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        var sql = $"SELECT embedding FROM {tableName} WHERE id = @id";
        var result = await conn.QuerySingleOrDefaultAsync<Vector>(sql, new { id = id.ToString() });

        return result?.ToArray();
    }

    public async Task<Dictionary<TKey, float[]>> GetEmbeddingsAsync(
        IEnumerable<TKey> ids,
        CancellationToken ct = default)
    {
        using var activity = PGVectorTelemetry.Activity.StartActivity("vector.getEmbeddings");
        var tableName = TableName;
        var idsList = ids.Select(x => x.ToString()).ToArray();

        if (idsList.Length == 0)
            return new Dictionary<TKey, float[]>();

        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        var sql = $"SELECT id, embedding FROM {tableName} WHERE id = ANY(@ids)";
        var results = await conn.QueryAsync<(string Id, Vector Embedding)>(sql, new { ids = idsList });

        return results.ToDictionary(
            x => (TKey)(object)x.Id,
            x => x.Embedding.ToArray());
    }

    public async Task<VectorQueryResult<TKey>> SearchAsync(
        VectorQueryOptions options,
        CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);

        using var activity = PGVectorTelemetry.Activity.StartActivity("vector.search");
        var tableName = TableName;

        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        // Build search query with appropriate distance operator
        var sql = new StringBuilder();
        var queryVector = new Vector(options.Query);

        sql.AppendLine($@"
            SELECT
                id,
                1 - (embedding {DistanceOperator} @embedding) AS score
            FROM {tableName}");

        var parameters = new DynamicParameters();
        parameters.Add("embedding", queryVector);

        // Apply filters if provided
        if (options.Filter != null)
        {
            sql.AppendLine("WHERE metadata @> @filter::jsonb");
            parameters.Add("filter", JsonConvert.SerializeObject(options.Filter));
        }

        // Order by similarity (closest first)
        sql.AppendLine($"ORDER BY embedding {DistanceOperator} @embedding");

        // Apply limit
        var limit = options.TopK ?? _options.DefaultTopK;
        sql.AppendLine($"LIMIT {limit}");

        var results = await conn.QueryAsync<(string Id, double Score)>(sql.ToString(), parameters);

        var searchResults = results.Select(x => new VectorResult<TKey>
        {
            Id = (TKey)(object)x.Id,
            Score = x.Score
        }).ToList();

        _logger?.LogDebug(
            "Vector search on {TableName} returned {Count} results (limit: {Limit}, metric: {Metric})",
            tableName,
            searchResults.Count,
            limit,
            _options.DistanceMetric);

        return new VectorQueryResult<TKey>
        {
            Results = searchResults,
            ContinuationToken = null // PostgreSQL doesn't need continuation for simple queries
        };
    }

    public async Task FlushAsync(CancellationToken ct = default)
    {
        using var activity = PGVectorTelemetry.Activity.StartActivity("vector.flush");
        var tableName = TableName;

        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        var sql = $"TRUNCATE TABLE {tableName}";
        await conn.ExecuteAsync(sql);

        _logger?.LogWarning("Flushed all vectors from {TableName}", tableName);
    }

    public async IAsyncEnumerable<VectorExportBatch<TKey>> ExportAllAsync(
        int? batchSize = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var activity = PGVectorTelemetry.Activity.StartActivity("vector.exportAll");
        var tableName = TableName;
        var effectiveBatchSize = batchSize ?? _options.ExportBatchSize;

        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        var sql = $@"
            SELECT id, embedding, metadata
            FROM {tableName}
            ORDER BY id
            LIMIT {effectiveBatchSize}
            OFFSET @offset";

        var offset = 0;

        while (true)
        {
            var batch = await conn.QueryAsync<(string Id, Vector Embedding, string? Metadata)>(
                sql,
                new { offset });

            var batchList = batch.ToList();
            if (batchList.Count == 0)
                break;

            var exportItems = batchList.Select(x => new VectorExportItem<TKey>
            {
                Id = (TKey)(object)x.Id,
                Embedding = x.Embedding.ToArray(),
                Metadata = x.Metadata != null
                    ? JsonConvert.DeserializeObject<Dictionary<string, object>>(x.Metadata)
                    : null
            }).ToList();

            yield return new VectorExportBatch<TKey>
            {
                Items = exportItems,
                Offset = offset,
                TotalExported = offset + exportItems.Count
            };

            if (batchList.Count < effectiveBatchSize)
                break;

            offset += effectiveBatchSize;
        }

        _logger?.LogInformation("Exported all vectors from {TableName}", tableName);
    }

    private async Task EnsureSchemaAsync(CancellationToken ct)
    {
        if (_schemaEnsured) return;
        await VectorEnsureCreatedAsync(ct);
    }
}
