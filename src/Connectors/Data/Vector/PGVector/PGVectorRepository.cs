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
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Vector.Abstractions;
using Koan.Data.Vector.Abstractions.Configuration;
using PgVec = Pgvector.Vector;

namespace Koan.Data.Connector.PGVector;

/// <summary>
/// PostgreSQL vector repository using the pgvector extension. Reference implementation of the
/// vector adapter contract (DATA-0097): zero-setup defaults, Reference = Intent registration,
/// self-reporting capabilities, and a real fail-loud metadata filter translator (no silent
/// match-all). Supports cosine/L2/inner-product similarity, HNSW + IVFFlat indexes, bulk ops, and
/// streaming export. Table names carry the "_vector" suffix per DATA-0087.
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

    // AI-0036 §10 / DATA-0097 P1: PGVector is the reference adapter — full operator set over JSONB.
    public Koan.Data.Abstractions.Filtering.VectorFilterCapabilities FilterCapabilities => PGVectorFilterTranslator.Caps;

    private string TableName => VectorAdapterNaming.GetOrCompute<TEntity, TKey>(_sp).ToLowerInvariant();

    private string DistanceOperator => _options.DistanceMetric switch
    {
        DistanceMetric.Cosine => "<=>",
        DistanceMetric.L2 => "<->",
        DistanceMetric.InnerProduct => "<#>",
        _ => "<=>"
    };

    public async Task VectorEnsureCreated(CancellationToken ct = default)
    {
        if (_schemaEnsured) return;

        using var activity = PGVectorTelemetry.Activity.StartActivity("vector.ensureCreated");
        var tableName = TableName;

        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        await _extensionManager.EnsureExtension(conn, ct);

        var exists = await conn.ExecuteScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM pg_tables WHERE schemaname = 'public' AND tablename = @name)",
            new { name = tableName });

        if (exists)
        {
            _schemaEnsured = true;
            _logger?.LogDebug("PGVector table {TableName} already exists", tableName);
            return;
        }

        if (!_extensionManager.ValidateDimension(_options.DefaultDimension))
        {
            throw new InvalidOperationException(
                $"Invalid dimension {_options.DefaultDimension}. Must be between 1 and {PGVectorOptions.MaxDimension}.");
        }

        var createTableSql = $@"
            CREATE TABLE {tableName} (
                id TEXT PRIMARY KEY,
                embedding vector({_options.DefaultDimension}),
                metadata JSONB,
                created_at TIMESTAMPTZ DEFAULT NOW(),
                updated_at TIMESTAMPTZ DEFAULT NOW()
            )";

        await conn.ExecuteAsync(createTableSql);
        _logger?.LogInformation("Created PGVector table {TableName} with dimension {Dimension}", tableName, _options.DefaultDimension);

        if (_options.AutoCreateIndex && _options.DefaultIndexType != IndexType.None)
        {
            var indexBuilder = new PgVectorIndexBuilder(conn, _options, _logger);
            switch (_options.DefaultIndexType)
            {
                case IndexType.Hnsw:
                    await indexBuilder.CreateHnswIndex(tableName, m: _options.HnswM, efConstruction: _options.HnswEfConstruction, ct: ct);
                    break;
                case IndexType.IvfFlat:
                    await indexBuilder.CreateIvfflatIndex(tableName, lists: _options.IvfFlatLists, ct: ct);
                    break;
            }
        }

        _schemaEnsured = true;
    }

    public async Task Upsert(TKey id, float[] embedding, object? metadata = null, CancellationToken ct = default)
    {
        await EnsureSchema(ct);
        using var activity = PGVectorTelemetry.Activity.StartActivity("vector.upsert");
        var tableName = TableName;

        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        var sql = $@"
            INSERT INTO {tableName} (id, embedding, metadata, updated_at)
            VALUES (@id, @embedding, @metadata::jsonb, NOW())
            ON CONFLICT (id) DO UPDATE
            SET embedding = EXCLUDED.embedding, metadata = EXCLUDED.metadata, updated_at = NOW()";

        await conn.ExecuteAsync(sql, new
        {
            id = id.ToString(),
            embedding = new PgVec(embedding),
            metadata = metadata != null ? JsonConvert.SerializeObject(metadata) : null
        });

        _logger?.LogTrace("Upserted vector {Id} to {TableName}", id, tableName);
    }

    public async Task<int> UpsertMany(IEnumerable<(TKey Id, float[] Embedding, object? Metadata)> items, CancellationToken ct = default)
    {
        await EnsureSchema(ct);
        using var activity = PGVectorTelemetry.Activity.StartActivity("vector.upsertMany");
        var tableName = TableName;
        var itemsList = items.ToList();
        if (itemsList.Count == 0) return 0;

        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        const int batchSize = 100;
        var totalInserted = 0;

        for (var i = 0; i < itemsList.Count; i += batchSize)
        {
            var batch = itemsList.Skip(i).Take(batchSize).ToList();
            var values = new StringBuilder();
            var parameters = new DynamicParameters();

            for (var j = 0; j < batch.Count; j++)
            {
                var item = batch[j];
                if (j > 0) values.Append(", ");
                values.Append($"(@id{j}, @embedding{j}, @metadata{j}::jsonb, NOW())");
                parameters.Add($"id{j}", item.Id.ToString());
                parameters.Add($"embedding{j}", new PgVec(item.Embedding));
                parameters.Add($"metadata{j}", item.Metadata != null ? JsonConvert.SerializeObject(item.Metadata) : null);
            }

            var sql = $@"
                INSERT INTO {tableName} (id, embedding, metadata, updated_at)
                VALUES {values}
                ON CONFLICT (id) DO UPDATE
                SET embedding = EXCLUDED.embedding, metadata = EXCLUDED.metadata, updated_at = NOW()";

            await conn.ExecuteAsync(sql, parameters);
            totalInserted += batch.Count;
        }

        _logger?.LogDebug("Upserted {Count} vectors to {TableName}", totalInserted, tableName);
        return totalInserted;
    }

    public async Task<bool> Delete(TKey id, CancellationToken ct = default)
    {
        using var activity = PGVectorTelemetry.Activity.StartActivity("vector.delete");
        var tableName = TableName;
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        var affected = await conn.ExecuteAsync($"DELETE FROM {tableName} WHERE id = @id", new { id = id.ToString() });
        _logger?.LogTrace("Deleted vector {Id} from {TableName} (affected: {Affected})", id, tableName, affected);
        return affected > 0;
    }

    public async Task<int> DeleteMany(IEnumerable<TKey> ids, CancellationToken ct = default)
    {
        using var activity = PGVectorTelemetry.Activity.StartActivity("vector.deleteMany");
        var tableName = TableName;
        var idsList = ids.Select(x => x.ToString()).ToArray();
        if (idsList.Length == 0) return 0;
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        var affected = await conn.ExecuteAsync($"DELETE FROM {tableName} WHERE id = ANY(@ids)", new { ids = idsList });
        _logger?.LogDebug("Deleted {Count} vectors from {TableName}", affected, tableName);
        return affected;
    }

    public async Task<float[]?> GetEmbedding(TKey id, CancellationToken ct = default)
    {
        using var activity = PGVectorTelemetry.Activity.StartActivity("vector.getEmbedding");
        var tableName = TableName;
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        var result = await conn.QuerySingleOrDefaultAsync<PgVec>($"SELECT embedding FROM {tableName} WHERE id = @id", new { id = id.ToString() });
        return result?.ToArray();
    }

    public async Task<Dictionary<TKey, float[]>> GetEmbeddings(IEnumerable<TKey> ids, CancellationToken ct = default)
    {
        using var activity = PGVectorTelemetry.Activity.StartActivity("vector.getEmbeddings");
        var tableName = TableName;
        var idsList = ids.Select(x => x.ToString()).ToArray();
        if (idsList.Length == 0) return new Dictionary<TKey, float[]>();
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        var results = await conn.QueryAsync<(string Id, PgVec Embedding)>($"SELECT id, embedding FROM {tableName} WHERE id = ANY(@ids)", new { ids = idsList });
        return results.ToDictionary(x => (TKey)(object)x.Id, x => x.Embedding.ToArray());
    }

    public async Task<VectorQueryResult<TKey>> Search(VectorQueryOptions options, CancellationToken ct = default)
    {
        await EnsureSchema(ct);
        using var activity = PGVectorTelemetry.Activity.StartActivity("vector.search");
        var tableName = TableName;

        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        var sql = new StringBuilder();
        sql.AppendLine($@"
            SELECT id, metadata, 1 - (embedding {DistanceOperator} @embedding) AS score
            FROM {tableName}");

        var parameters = new DynamicParameters();
        parameters.Add("embedding", new PgVec(options.Query));

        // AI-0036 §10 / DATA-0097 P1: options.Filter is the typed, coordinator-validated (fully
        // pushable) unified Filter. Render it straight to SQL — no re-parse, no silent fallback.
        var where = PGVectorFilterTranslator.Translate(options.Filter, parameters);
        if (!string.IsNullOrEmpty(where))
            sql.AppendLine($"WHERE {where}");

        sql.AppendLine($"ORDER BY embedding {DistanceOperator} @embedding");
        var limit = options.TopK ?? _options.DefaultTopK;
        sql.AppendLine($"LIMIT {limit}");

        var rows = await conn.QueryAsync<(string Id, string? Metadata, double Score)>(sql.ToString(), parameters);

        var matches = rows.Select(x => new VectorMatch<TKey>(
            (TKey)(object)x.Id,
            x.Score,
            x.Metadata != null ? JsonConvert.DeserializeObject<Dictionary<string, object>>(x.Metadata) : null)).ToList();

        _logger?.LogDebug("Vector search on {TableName} returned {Count} results (limit: {Limit}, metric: {Metric})",
            tableName, matches.Count, limit, _options.DistanceMetric);

        return new VectorQueryResult<TKey>(matches, ContinuationToken: null, TotalKind: VectorTotalKind.Unknown);
    }

    public async Task Flush(CancellationToken ct = default)
    {
        using var activity = PGVectorTelemetry.Activity.StartActivity("vector.flush");
        var tableName = TableName;
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await conn.ExecuteAsync($"TRUNCATE TABLE {tableName}");
        _logger?.LogWarning("Flushed all vectors from {TableName}", tableName);
    }

    public async IAsyncEnumerable<VectorExportBatch<TKey>> ExportAll(int? batchSize = null, [EnumeratorCancellation] CancellationToken ct = default)
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
            var rows = (await conn.QueryAsync<(string Id, PgVec Embedding, string? Metadata)>(sql, new { offset })).ToList();
            if (rows.Count == 0) break;

            foreach (var x in rows)
            {
                yield return new VectorExportBatch<TKey>(
                    (TKey)(object)x.Id,
                    x.Embedding.ToArray(),
                    x.Metadata != null ? JsonConvert.DeserializeObject<Dictionary<string, object>>(x.Metadata) : null);
            }

            if (rows.Count < effectiveBatchSize) break;
            offset += effectiveBatchSize;
        }

        _logger?.LogInformation("Exported all vectors from {TableName}", tableName);
    }

    private async Task EnsureSchema(CancellationToken ct)
    {
        if (_schemaEnsured) return;
        await VectorEnsureCreated(ct);
    }
}
