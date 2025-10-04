using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Koan.Core;
using Koan.Core.Infrastructure;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Instructions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Core;
using Koan.Data.Core.Optimization;
using Koan.Data.Core.Schema;
using Koan.Data.Relational.Linq;
using Koan.Data.Relational.Orchestration;
using System.Linq.Expressions;
using Newtonsoft.Json;

namespace Koan.Data.Connector.Postgres;

internal sealed class PostgresRepository<TEntity, TKey> :
    IDataRepository<TEntity, TKey>,
    IOptimizedDataRepository<TEntity, TKey>,
    ILinqQueryRepository<TEntity, TKey>,
    IStringQueryRepository<TEntity, TKey>,
    IDataRepositoryWithOptions<TEntity, TKey>,
    ILinqQueryRepositoryWithOptions<TEntity, TKey>,
    IStringQueryRepositoryWithOptions<TEntity, TKey>,
    IQueryCapabilities,
    IWriteCapabilities,
    IBulkDelete<TKey>,
    IInstructionExecutor<TEntity>,
    ISchemaHealthContributor<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    public QueryCapabilities Capabilities => QueryCapabilities.Linq | QueryCapabilities.String;
    public WriteCapabilities Writes => WriteCapabilities.AtomicBatch | WriteCapabilities.BulkDelete | WriteCapabilities.FastRemove;

    // Storage optimization support
    private readonly StorageOptimizationInfo _optimizationInfo;
    public StorageOptimizationInfo OptimizationInfo => _optimizationInfo;

    private readonly IServiceProvider _sp;
    private readonly PostgresOptions _options;
    private readonly IStorageNameResolver _nameResolver;
    private readonly StorageNameResolver.Convention _conv;
    private readonly ILinqSqlDialect _dialect = new PgDialect();
    private readonly int _defaultPageSize;
    private readonly int _maxPageSize;
    private readonly ILogger _logger;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _healthyCache = new(StringComparer.Ordinal);

    public PostgresRepository(IServiceProvider sp, PostgresOptions options, IStorageNameResolver resolver)
    {
        _sp = sp;
        _options = options;
        _nameResolver = resolver;

        // Get storage optimization info from AggregateBag
        _optimizationInfo = sp.GetStorageOptimization<TEntity, TKey>();

        // DEBUG: MediaFormat specific logging
        if (typeof(TEntity).Name == "MediaFormat")
        {
            Console.WriteLine($"[REPOSITORY-DEBUG] PostgresRepository<MediaFormat> - Retrieved optimization info:");
            Console.WriteLine($"[REPOSITORY-DEBUG] MediaFormat - OptimizationType: {_optimizationInfo.OptimizationType}");
            Console.WriteLine($"[REPOSITORY-DEBUG] MediaFormat - Reason: {_optimizationInfo.Reason}");
            Console.WriteLine($"[REPOSITORY-DEBUG] MediaFormat - IdPropertyName: {_optimizationInfo.IdPropertyName}");
        }

        KoanEnv.TryInitialize(sp);
        _logger = (sp.GetService(typeof(ILogger<PostgresRepository<TEntity, TKey>>)) as ILogger)
                  ?? (sp.GetService(typeof(ILoggerFactory)) is ILoggerFactory lf
                      ? lf.CreateLogger($"Koan.Data.Connector.Postgres[{typeof(TEntity).FullName}]")
                      : NullLogger.Instance);
        _conv = new StorageNameResolver.Convention(options.NamingStyle, options.Separator, NameCasing.AsIs);
        _defaultPageSize = options.DefaultPageSize > 0 ? options.DefaultPageSize : 50;
        _maxPageSize = options.MaxPageSize > 0 ? options.MaxPageSize : 200;

        // Log optimization strategy for diagnostics
        if (_optimizationInfo.IsOptimized)
        {
            _logger.LogInformation("PostgreSQL Repository Optimization: Entity={EntityType}, OptimizationType={OptimizationType}",
                typeof(TEntity).Name, _optimizationInfo.OptimizationType);
        }
    }

    private string TableName => Core.Configuration.StorageNameRegistry.GetOrCompute<TEntity, TKey>(_sp);
    private string QualifiedTable => (_options.SearchPath is { Length: > 0 } sp ? $"\"{sp}\"." : string.Empty) + "\"" + TableName.Replace("\"", "\"\"") + "\"";

    /// <summary>
    /// Applies storage optimization to entity before writing to PostgreSQL.
    /// Simple pre-write transformation - no complex serialization needed.
    /// </summary>
    private static void OptimizeEntityForStorage(TEntity entity, StorageOptimizationInfo optimizationInfo)
    {
        // Only optimize if needed and this is a string-keyed entity
        if (!optimizationInfo.IsOptimized || typeof(TKey) != typeof(string))
            return;

        // Get the current string ID value
        var idProperty = typeof(TEntity).GetProperty(optimizationInfo.IdPropertyName);
        if (idProperty?.GetValue(entity) is not string stringId || string.IsNullOrEmpty(stringId))
            return;

        // Apply optimization based on type
        switch (optimizationInfo.OptimizationType)
        {
            case StorageOptimizationType.Guid:
                // For PostgreSQL, convert GUID string to PostgreSQL UUID format for efficient storage
                if (Guid.TryParse(stringId, out var guid))
                {
                    // PostgreSQL UUID format is efficient for storage and indexing
                    idProperty.SetValue(entity, guid.ToString("D")); // Ensure standard format
                }
                break;

            // Future optimization types would go here
        }
    }

    private NpgsqlConnection Open()
    {
        var conn = new NpgsqlConnection(_options.ConnectionString);
        conn.Open();
        EnsureOrchestrated(conn);
        return conn;
    }

    private static string BuildCacheKey(NpgsqlConnection conn, string table)
    {
        try
        {
            return $"{conn.Host}/{conn.Database}::{table}";
        }
        catch
        {
            return $"{conn.ConnectionString}::{table}";
        }
    }

    private void EnsureOrchestrated(NpgsqlConnection conn)
    {
        var table = TableName;
        var cacheKey = BuildCacheKey(conn, table);
        try
        {
            EnsureOrchestratedAsync(conn, cacheKey, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch
        {
            // best effort; let downstream operations surface errors if schema is missing
        }
    }

    private Task EnsureOrchestratedAsync(NpgsqlConnection conn, string cacheKey, CancellationToken ct)
    {
        var table = TableName;
        if (_healthyCache.TryGetValue(cacheKey, out var healthy) && healthy)
        {
            return Task.CompletedTask;
        }

        return Singleflight.RunAsync(cacheKey, async runCt =>
        {
            if (_healthyCache.TryGetValue(cacheKey, out var cached) && cached)
            {
                return;
            }

            try
            {
                var orch = (IRelationalSchemaOrchestrator)_sp.GetRequiredService(typeof(IRelationalSchemaOrchestrator));
                var ddl = new PgDdlExecutor(conn, _options.SearchPath);
                var feats = new PostgresStoreFeatures();
                var report = (IDictionary<string, object?>)await orch.ValidateAsync<TEntity, TKey>(ddl, feats, runCt).ConfigureAwait(false);
                var ddlAllowed = report.TryGetValue("DdlAllowed", out var da) && da is bool allowed && allowed;
                var tableExists = report.TryGetValue("TableExists", out var te) && te is bool exists && exists;
                if (ddlAllowed)
                {
                    await orch.EnsureCreatedAsync<TEntity, TKey>(ddl, feats, runCt).ConfigureAwait(false);
                    _healthyCache[cacheKey] = true;
                    return;
                }

                if (tableExists)
                {
                    _healthyCache[cacheKey] = true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Postgres schema ensure failed for {Table}", table);
                _healthyCache.TryRemove(cacheKey, out _);
                throw;
            }
        }, ct);
    }

    public async Task EnsureHealthyAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var table = TableName;
        await using var conn = new NpgsqlConnection(_options.ConnectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        var cacheKey = BuildCacheKey(conn, table);
        try
        {
            await EnsureOrchestratedAsync(conn, cacheKey, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Postgres ensure healthy failed for {Table}", table);
            throw;
        }
    }

    public void InvalidateHealth()
    {
        var suffix = $"::{TableName}";
        foreach (var key in _healthyCache.Keys)
        {
            if (key.EndsWith(suffix, StringComparison.Ordinal))
            {
                _healthyCache.TryRemove(key, out _);
            }
        }
    }

    private void EnsureTable(NpgsqlConnection conn)
    {
        bool entityReadOnly = typeof(TEntity).GetCustomAttributes(typeof(System.ComponentModel.ReadOnlyAttribute), inherit: true).Any();
        bool allowDdl = IsDdlAllowed(entityReadOnly);
        if (!allowDdl)
        {
            if (TableExists(conn)) return;
            return;
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"CREATE TABLE IF NOT EXISTS {QualifiedTable} (
            ""Id"" text PRIMARY KEY,
            ""Json"" jsonb NOT NULL
        );";
        cmd.ExecuteNonQuery();

        // Create expression indexes for projected properties if marked [Index]
        var projections = ProjectionResolver.Get(typeof(TEntity));
        foreach (var p in projections)
        {
            if (!p.IsIndexed) continue;
            var col = p.ColumnName;
            var path = p.Property.Name;
            var idxName = $"ix_{TableName}_{col}".ToLowerInvariant();
            TryCreateExpressionIndex(conn, idxName, path);
        }
    }

    private void TryCreateExpressionIndex(NpgsqlConnection conn, string indexName, string propPath)
    {
        try
        {
            using var idx = conn.CreateCommand();
            // index on extracted text value for ordering/filtering
            idx.CommandText = $"CREATE INDEX IF NOT EXISTS \"{indexName}\" ON {QualifiedTable} ((\"Json\" #>> '{{{propPath}}}'))";
            idx.ExecuteNonQuery();
        }
        catch { }
    }

    private bool TableExists(NpgsqlConnection conn)
    {
        using var cmd = conn.CreateCommand();
        var (schema, table) = ResolveSchemaAndTable();
        cmd.CommandText = "SELECT 1 FROM information_schema.tables WHERE table_schema = @s AND table_name = @t";
        cmd.Parameters.AddWithValue("s", schema);
        cmd.Parameters.AddWithValue("t", table);
        try { var o = cmd.ExecuteScalar(); return o != null; } catch { return false; }
    }

    private (string schema, string table) ResolveSchemaAndTable()
    {
        var schema = _options.SearchPath ?? "public";
        var table = TableName;
        return (schema, table);
    }

    private static TEntity FromRow((string Id, string Json) row)
        => JsonConvert.DeserializeObject<TEntity>(row.Json)!;

    private (object Id, string Json) ToRowOptimized(TEntity e)
    {
        var json = JsonConvert.SerializeObject(e);
        var stringId = e.Id!.ToString()!;
        // Apply storage optimization before serialization
        OptimizeEntityForStorage(e, _optimizationInfo);
        var optimizedId = e.Id!.ToString()!;
        return (optimizedId, json);
    }

    // Legacy method for compatibility - now uses optimization internally
    private (string Id, string Json) ToRow(TEntity e)
    {
        var optimized = ToRowOptimized(e);
        return (optimized.Id.ToString()!, optimized.Json);
    }

    public async Task<TEntity?> GetAsync(TKey id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var act = PgTelemetry.Activity.StartActivity("pg.get");
        act?.SetTag("entity", typeof(TEntity).FullName);
        await using var conn = Open();

        // Use optimized ID conversion for query parameter
        // Apply optimization to the ID for query parameter
        string optimizedId;
        if (_optimizationInfo.IsOptimized && typeof(TKey) == typeof(string) && id is string stringId)
        {
            if (_optimizationInfo.OptimizationType == StorageOptimizationType.Guid && Guid.TryParse(stringId, out var guid))
                optimizedId = guid.ToString("D");
            else
                optimizedId = stringId;
        }
        else
        {
            optimizedId = id!.ToString()!;
        }

        // Note: The Id column in SELECT should be converted back to string for entity hydration
        // The WHERE clause uses the optimized storage type
        var row = await conn.QuerySingleOrDefaultAsync<(string Id, string Json)>(
            $"SELECT \"Id\"::text, \"Json\"::text FROM {QualifiedTable} WHERE \"Id\" = @Id",
            new { Id = optimizedId });

        return row == default ? null : FromRow(row);
    }

    public async Task<IReadOnlyList<TEntity>> QueryAsync(object? query, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var act = PgTelemetry.Activity.StartActivity("pg.query:all");
        act?.SetTag("entity", typeof(TEntity).FullName);
        await using var conn = Open();
        // DATA-0061: no-options should return full set
        var rows = await conn.QueryAsync<(string Id, string Json)>($"SELECT \"Id\", \"Json\"::text FROM {QualifiedTable} ORDER BY \"Id\"");
        return rows.Select(FromRow).ToList();
    }

    public async Task<IReadOnlyList<TEntity>> QueryAsync(object? query, DataQueryOptions? options, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var act = PgTelemetry.Activity.StartActivity("pg.query:all+opts");
        act?.SetTag("entity", typeof(TEntity).FullName);
        var (offset, limit) = ComputeSkipTake(options);
        await using var conn = Open();
        var sql = $"SELECT \"Id\", \"Json\"::text FROM {QualifiedTable} ORDER BY \"Id\" LIMIT {limit} OFFSET {offset}";
        var rows = await conn.QueryAsync<(string Id, string Json)>(sql);
        return rows.Select(FromRow).ToList();
    }

    public async Task<CountResult> CountAsync(CountRequest<TEntity> request, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var act = PgTelemetry.Activity.StartActivity("pg.count");
        act?.SetTag("entity", typeof(TEntity).FullName);
        await using var conn = Open();

        // Fast count via pg_stat when no predicate and strategy allows it
        if (request.Predicate is null && request.RawQuery is null && request.ProviderQuery is null)
        {
            var strategy = request.Options?.CountStrategy ?? CountStrategy.Optimized;
            if (strategy == CountStrategy.Fast || strategy == CountStrategy.Optimized)
            {
                try
                {
                    var (schema, table) = ResolveSchemaAndTable();
                    var estimate = await conn.ExecuteScalarAsync<long>(
                        @"SELECT n_live_tup FROM pg_stat_user_tables
                          WHERE schemaname = @schema AND relname = @table",
                        new { schema, table });
                    if (estimate >= 0)
                        return CountResult.Estimate(estimate);
                }
                catch
                {
                    // Fall back to exact count
                }
            }
        }

        // Exact count based on request type
        if (request.Predicate is not null)
        {
            var translator = new LinqWhereTranslator<TEntity>(_dialect);
            try
            {
                var (whereSql, parameters) = translator.Translate(request.Predicate);
                whereSql = RewriteWhereForProjection(whereSql);
                return await CountWhereAsync(whereSql, parameters);
            }
            catch (NotSupportedException)
            {
                var all = await QueryAsync((object?)null, ct);
                var count = (long)all.AsQueryable().Count(request.Predicate);
                return CountResult.Exact(count);
            }
        }

        if (request.RawQuery is not null)
        {
            var whereSql = RewriteWhereForProjection(request.RawQuery);
            var count = await conn.ExecuteScalarAsync<long>($"SELECT COUNT(1) FROM {QualifiedTable} WHERE " + whereSql);
            return CountResult.Exact(count);
        }

        // No predicate - full table count
        var totalCount = await conn.ExecuteScalarAsync<long>($"SELECT COUNT(1) FROM {QualifiedTable}");
        return CountResult.Exact(totalCount);
    }

    public async Task<IReadOnlyList<TEntity>> QueryAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var act = PgTelemetry.Activity.StartActivity("pg.query:linq");
        act?.SetTag("entity", typeof(TEntity).FullName);
        var translator = new LinqWhereTranslator<TEntity>(_dialect);
        try
        {
            var (whereSql, parameters) = translator.Translate(predicate);
            whereSql = RewriteWhereForProjection(whereSql);
            await using var conn = Open();
            // DATA-0061: no-options should return full set for predicate
            var sql = $"SELECT \"Id\", \"Json\"::text FROM {QualifiedTable} WHERE {whereSql} ORDER BY \"Id\"";
            var dyn = new DynamicParameters();
            for (int i = 0; i < parameters.Count; i++) dyn.Add($"p{i}", parameters[i]);
            var rows = await conn.QueryAsync<(string Id, string Json)>(sql, dyn);
            return rows.Select(FromRow).ToList();
        }
        catch (NotSupportedException)
        {
            var all = await QueryAsync((object?)null, ct);
            return all.AsQueryable().Where(predicate).ToList();
        }
    }

    public async Task<IReadOnlyList<TEntity>> QueryAsync(Expression<Func<TEntity, bool>> predicate, DataQueryOptions? options, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var act = PgTelemetry.Activity.StartActivity("pg.query:linq+opts");
        act?.SetTag("entity", typeof(TEntity).FullName);
        var translator = new LinqWhereTranslator<TEntity>(_dialect);
        try
        {
            var (whereSql, parameters) = translator.Translate(predicate);
            whereSql = RewriteWhereForProjection(whereSql);
            var (offset, limit) = ComputeSkipTake(options);
            await using var conn = Open();
            var sql = $"SELECT \"Id\", \"Json\"::text FROM {QualifiedTable} WHERE {whereSql} ORDER BY \"Id\" LIMIT {limit} OFFSET {offset}";
            var dyn = new DynamicParameters();
            for (int i = 0; i < parameters.Count; i++) dyn.Add($"p{i}", parameters[i]);
            var rows = await conn.QueryAsync<(string Id, string Json)>(sql, dyn);
            return rows.Select(FromRow).ToList();
        }
        catch (NotSupportedException)
        {
            var all = await QueryAsync((object?)null, options, ct);
            return all.AsQueryable().Where(predicate).ToList();
        }
    }

    private async Task<CountResult> CountWhereAsync(string whereSql, IReadOnlyList<object?> parameters)
    {
        await using var conn = Open();
        var sql = $"SELECT COUNT(1) FROM {QualifiedTable} WHERE {whereSql}";
        var dyn = new DynamicParameters();
        for (int i = 0; i < parameters.Count; i++) dyn.Add($"p{i}", parameters[i]);
        var count = await conn.ExecuteScalarAsync<long>(sql, dyn);
        return CountResult.Exact(count);
    }

    public async Task<IReadOnlyList<TEntity>> QueryAsync(string sql, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var act = PgTelemetry.Activity.StartActivity("pg.query:string");
        act?.SetTag("entity", typeof(TEntity).FullName);
        await using var conn = Open();
        if (IsFullSelect(sql))
        {
            var rewritten = RewriteEntityToken(sql);
            var rows = await conn.QueryAsync(rewritten);
            return MapRowsToEntities(rows);
        }
        else
        {
            var whereSql = RewriteWhereForProjection(sql);
            var rows = await conn.QueryAsync<(string Id, string Json)>($"SELECT \"Id\", \"Json\"::text FROM {QualifiedTable} WHERE " + whereSql + $" ORDER BY \"Id\" LIMIT {_defaultPageSize} OFFSET 0");
            return rows.Select(FromRow).ToList();
        }
    }

    public async Task<IReadOnlyList<TEntity>> QueryAsync(string sql, object? parameters, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var act = PgTelemetry.Activity.StartActivity("pg.query:string:param");
        act?.SetTag("entity", typeof(TEntity).FullName);
        await using var conn = Open();
        if (IsFullSelect(sql))
        {
            var rewritten = RewriteEntityToken(sql);
            var rows = await conn.QueryAsync(rewritten, parameters);
            return MapRowsToEntities(rows);
        }
        else
        {
            var whereSql = RewriteWhereForProjection(sql);
            var rows = await conn.QueryAsync<(string Id, string Json)>($"SELECT \"Id\", \"Json\"::text FROM {QualifiedTable} WHERE " + whereSql + $" ORDER BY \"Id\" LIMIT {_defaultPageSize} OFFSET 0", parameters);
            return rows.Select(FromRow).ToList();
        }
    }

    public async Task<IReadOnlyList<TEntity>> QueryAsync(string sql, DataQueryOptions? options, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var act = PgTelemetry.Activity.StartActivity("pg.query:string+opts");
        act?.SetTag("entity", typeof(TEntity).FullName);
        await using var conn = Open();
        if (IsFullSelect(sql))
        {
            var rewritten = RewriteEntityToken(sql);
            var rows = await conn.QueryAsync(rewritten);
            return MapRowsToEntities(rows);
        }
        else
        {
            var (offset, limit) = ComputeSkipTake(options);
            var whereSql = RewriteWhereForProjection(sql);
            var rows = await conn.QueryAsync<(string Id, string Json)>($"SELECT \"Id\", \"Json\"::text FROM {QualifiedTable} WHERE " + whereSql + $" ORDER BY \"Id\" LIMIT {limit} OFFSET {offset}");
            return rows.Select(FromRow).ToList();
        }
    }

    public async Task<IReadOnlyList<TEntity>> QueryAsync(string sql, object? parameters, DataQueryOptions? options, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var act = PgTelemetry.Activity.StartActivity("pg.query:string:param+opts");
        act?.SetTag("entity", typeof(TEntity).FullName);
        await using var conn = Open();
        if (IsFullSelect(sql))
        {
            var rewritten = RewriteEntityToken(sql);
            var rows = await conn.QueryAsync(rewritten, parameters);
            return MapRowsToEntities(rows);
        }
        else
        {
            var (offset, limit) = ComputeSkipTake(options);
            var whereSql = RewriteWhereForProjection(sql);
            var rows = await conn.QueryAsync<(string Id, string Json)>($"SELECT \"Id\", \"Json\"::text FROM {QualifiedTable} WHERE " + whereSql + $" ORDER BY \"Id\" LIMIT {limit} OFFSET {offset}", parameters);
            return rows.Select(FromRow).ToList();
        }
    }


    public async Task<TEntity> UpsertAsync(TEntity model, CancellationToken ct = default)
    { await UpsertManyAsync(new[] { model }, ct); return model; }

    public async Task<bool> DeleteAsync(TKey id, CancellationToken ct = default)
        => await DeleteManyAsync(new[] { id }, ct).ContinueWith(t => t.Result > 0, ct);

    public async Task<int> UpsertManyAsync(IEnumerable<TEntity> models, CancellationToken ct = default)
    {
        await using var conn = Open();
        await using var tx = await conn.BeginTransactionAsync(ct);
        var count = 0;
        foreach (var e in models)
        {
            ct.ThrowIfCancellationRequested();
            var row = ToRowOptimized(e);
            var affected = await conn.ExecuteAsync($"INSERT INTO {QualifiedTable} (\"Id\", \"Json\") VALUES (@Id, CAST(@Json AS jsonb)) ON CONFLICT (\"Id\") DO UPDATE SET \"Json\" = EXCLUDED.\"Json\"", new { row.Id, row.Json }, tx);
            count += affected > 0 ? 1 : 0;
        }
        await tx.CommitAsync(ct);
        return count;
    }

    public async Task<int> DeleteManyAsync(IEnumerable<TKey> ids, CancellationToken ct = default)
    {
        await using var conn = Open();
        // Apply optimization to all IDs for bulk delete
        var optimizedIds = ids.Select(id =>
        {
            if (_optimizationInfo.IsOptimized && typeof(TKey) == typeof(string) && id is string stringId)
            {
                if (_optimizationInfo.OptimizationType == StorageOptimizationType.Guid && Guid.TryParse(stringId, out var guid))
                    return guid.ToString("D");
                else
                    return stringId;
            }
            return id!.ToString()!;
        }).ToArray();
        return await conn.ExecuteAsync($"DELETE FROM {QualifiedTable} WHERE \"Id\" = ANY(@Ids)", new { Ids = optimizedIds });
    }

    public async Task<int> DeleteAllAsync(CancellationToken ct = default)
    {
        await using var conn = Open();
        return await conn.ExecuteAsync($"DELETE FROM {QualifiedTable}");
    }

    public async Task<long> RemoveAllAsync(RemoveStrategy strategy, CancellationToken ct = default)
    {
        await using var conn = Open();

        // Resolve Optimized strategy based on provider capabilities
        var effectiveStrategy = strategy == RemoveStrategy.Optimized
            ? (Writes.HasFlag(WriteCapabilities.FastRemove) ? RemoveStrategy.Fast : RemoveStrategy.Safe)
            : strategy;

        if (effectiveStrategy == RemoveStrategy.Fast)
        {
            // Fast path: TRUNCATE (bypasses hooks, resets sequence)
            try
            {
                await conn.ExecuteAsync($"TRUNCATE TABLE {QualifiedTable} RESTART IDENTITY", ct);
                return -1; // TRUNCATE doesn't report count
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "0A000") // Feature not supported
            {
                // Foreign key constraint - fall back to DELETE
                // Silently fall through to safe path
            }
        }

        // Safe path: DELETE (fires hooks if registered)
        var countRequest = new CountRequest<TEntity>();
        var countResult = await CountAsync(countRequest, ct);
        await conn.ExecuteAsync($"DELETE FROM {QualifiedTable}", ct);
        return countResult.Value;
    }

    public IBatchSet<TEntity, TKey> CreateBatch() => new PgBatch(this);

    private sealed class PgBatch(PostgresRepository<TEntity, TKey> repo) : IBatchSet<TEntity, TKey>
    {
        private readonly List<TEntity> _adds = new();
        private readonly List<TEntity> _updates = new();
        private readonly List<TKey> _deletes = new();
        private readonly List<(TKey id, Action<TEntity> mutate)> _mutations = new();

        public IBatchSet<TEntity, TKey> Add(TEntity entity) { _adds.Add(entity); return this; }
        public IBatchSet<TEntity, TKey> Update(TEntity entity) { _updates.Add(entity); return this; }
        public IBatchSet<TEntity, TKey> Update(TKey id, Action<TEntity> mutate) { _mutations.Add((id, mutate)); return this; }
        public IBatchSet<TEntity, TKey> Delete(TKey id) { _deletes.Add(id); return this; }
        public IBatchSet<TEntity, TKey> Clear() { _adds.Clear(); _updates.Clear(); _deletes.Clear(); _mutations.Clear(); return this; }

        public async Task<BatchResult> SaveAsync(BatchOptions? options = null, CancellationToken ct = default)
        {
            foreach (var (id, mutate) in _mutations)
            {
                var current = await repo.GetAsync(id, ct);
                if (current is not null) { mutate(current); _updates.Add(current); }
            }
            var upserts = _adds.Concat(_updates);
            var added = _adds.Count; var updated = _updates.Count;
            var requireAtomic = options?.RequireAtomic == true;
            if (!requireAtomic)
            {
                if (upserts.Any()) await repo.UpsertManyAsync(upserts, ct);
                var deleted = 0; if (_deletes.Any()) deleted = await repo.DeleteManyAsync(_deletes, ct);
                return new BatchResult(added, updated, deleted);
            }

            await using var conn = repo.Open();
            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                foreach (var e in upserts)
                {
                    ct.ThrowIfCancellationRequested();
                    var row = repo.ToRow(e);
                    await conn.ExecuteAsync($"INSERT INTO {repo.QualifiedTable} (\"Id\", \"Json\") VALUES (@Id, CAST(@Json AS jsonb)) ON CONFLICT (\"Id\") DO UPDATE SET \"Json\" = EXCLUDED.\"Json\"", new { row.Id, row.Json }, tx);
                }
                var deleted = 0;
                if (_deletes.Any())
                {
                    deleted = await conn.ExecuteAsync($"DELETE FROM {repo.QualifiedTable} WHERE \"Id\" = ANY(@Ids)", new { Ids = _deletes.Select(i => i!.ToString()!).ToArray() }, tx);
                }
                await tx.CommitAsync(ct);
                return new BatchResult(added, updated, deleted);
            }
            catch
            {
                try { await tx.RollbackAsync(ct); } catch { }
                throw;
            }
        }
    }

    public async Task<TResult> ExecuteAsync<TResult>(Instruction instruction, CancellationToken ct = default)
    {
        using var act = PgTelemetry.Activity.StartActivity("pg.instruction");
        act?.SetTag("entity", typeof(TEntity).FullName);
        await using var conn = new NpgsqlConnection(_options.ConnectionString);
        await conn.OpenAsync(ct);
        switch (instruction.Name)
        {
            case RelationalInstructions.SchemaValidate:
                {
                    var orch = (IRelationalSchemaOrchestrator)_sp.GetRequiredService(typeof(IRelationalSchemaOrchestrator));
                    var ddl = new PgDdlExecutor(conn, _options.SearchPath);
                    var feats = new PostgresStoreFeatures();
                    var report = await orch.ValidateAsync<TEntity, TKey>(ddl, feats, ct);
                    return (TResult)report;
                }
            case DataInstructions.EnsureCreated:
            case RelationalInstructions.SchemaEnsureCreated:
                {
                    var orch = (IRelationalSchemaOrchestrator)_sp.GetRequiredService(typeof(IRelationalSchemaOrchestrator));
                    var ddl = new PgDdlExecutor(conn, _options.SearchPath);
                    var feats = new PostgresStoreFeatures();
                    var key = $"{conn.Host}/{conn.Database}::{TableName}";
                    await Singleflight.RunAsync(key, async kct =>
                    {
                        await orch.EnsureCreatedAsync<TEntity, TKey>(ddl, feats, kct);
                        _healthyCache[key] = true;
                    }, ct);
                    object ok = true; return (TResult)ok;
                }
            case RelationalInstructions.SchemaClear:
            case DataInstructions.Clear:
                {
                    EnsureTable(conn);
                    var del = await conn.ExecuteAsync($"DELETE FROM {QualifiedTable}");
                    try { var key = $"{conn.Host}/{conn.Database}::{TableName}"; _healthyCache.TryRemove(key, out _); } catch { }
                    object res = del; return (TResult)res;
                }
            case RelationalInstructions.SqlScalar:
                {
                    var sql = RewriteEntityToken(GetSqlFromInstruction(instruction));
                    var p = GetParamsFromInstruction(instruction);
                    var result = await conn.ExecuteScalarAsync(sql, p);
                    return CastScalar<TResult>(result);
                }
            case RelationalInstructions.SqlNonQuery:
                {
                    var sql = RewriteEntityToken(GetSqlFromInstruction(instruction));
                    var p = GetParamsFromInstruction(instruction);
                    var affected = await conn.ExecuteAsync(sql, p);
                    object res = affected; return (TResult)res;
                }
            case RelationalInstructions.SqlQuery:
                {
                    var sql = RewriteEntityToken(GetSqlFromInstruction(instruction));
                    var p = GetParamsFromInstruction(instruction);
                    var rows = await conn.QueryAsync(sql, p);
                    var list = MapDynamicRows(rows);
                    return (TResult)(object)list;
                }
            default:
                throw new NotSupportedException($"Instruction '{instruction.Name}' not supported by Postgres adapter for {typeof(TEntity).Name}.");
        }
    }

    private object ValidateSchema(NpgsqlConnection conn)
    {
        bool entityReadOnly = typeof(TEntity).GetCustomAttributes(typeof(System.ComponentModel.ReadOnlyAttribute), inherit: true).Any();
        bool ddlAllowed = IsDdlAllowed(entityReadOnly);
        var (schema, _) = ResolveSchemaAndTable();
        var table = TableName;
        var exists = TableExists(conn);
        var projections = ProjectionResolver.Get(typeof(TEntity));
        var projectedColumns = projections.Select(p => p.ColumnName).Distinct(StringComparer.Ordinal).ToArray();
        var state = exists ? "Healthy" : (_options.SchemaMatching == SchemaMatchingMode.Strict ? "Unhealthy" : "Degraded");
        return new Dictionary<string, object?>
        {
            ["Provider"] = "postgres",
            ["Schema"] = schema,
            ["Table"] = table,
            ["TableExists"] = exists,
            ["ProjectedColumns"] = projectedColumns,
            ["Policy"] = _options.DdlPolicy.ToString(),
            ["DdlAllowed"] = ddlAllowed,
            ["MatchingMode"] = _options.SchemaMatching.ToString(),
            ["State"] = state
        };
    }

    private static string GetSqlFromInstruction(Instruction instruction)
    {
        var payload = instruction.Payload;
        var sqlProp = payload?.GetType().GetProperty("Sql");
        var sql = sqlProp?.GetValue(payload) as string;
        if (string.IsNullOrWhiteSpace(sql)) throw new ArgumentException("Instruction payload missing Sql.");
        return sql!;
    }
    private static object? GetParamsFromInstruction(Instruction instruction)
        => instruction.Parameters is null ? null : new DynamicParameters(new Dictionary<string, object?>(instruction.Parameters));

    private static bool IsFullSelect(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return false;
        var s = sql.TrimStart();
        return s.StartsWith("select ", StringComparison.OrdinalIgnoreCase);
    }

    // Relational orchestration primitives for Postgres
    private sealed class PgDdlExecutor(NpgsqlConnection conn, string? searchPath) : IRelationalDdlExecutor
    {
        private string Qual(string ident) => ident.Replace("\"", "\"\"");
        public bool TableExists(string schema, string table)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM information_schema.tables WHERE table_schema = @s AND table_name = @t";
            cmd.Parameters.AddWithValue("@s", schema ?? (searchPath ?? "public"));
            cmd.Parameters.AddWithValue("@t", table);
            return cmd.ExecuteScalar() is not null;
        }
        public bool ColumnExists(string schema, string table, string column)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM information_schema.columns WHERE table_schema = @s AND table_name = @t AND column_name = @c";
            cmd.Parameters.AddWithValue("@s", schema ?? (searchPath ?? "public"));
            cmd.Parameters.AddWithValue("@t", table);
            cmd.Parameters.AddWithValue("@c", column);
            return cmd.ExecuteScalar() is not null;
        }
        public void CreateTableIdJson(string schema, string table, string idColumn = "Id", string jsonColumn = "Json")
        {
            var sch = schema ?? (searchPath ?? "public");
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"CREATE TABLE IF NOT EXISTS \"{Qual(sch)}\".\"{Qual(table)}\" (\"{Qual(idColumn)}\" text PRIMARY KEY, \"{Qual(jsonColumn)}\" jsonb NOT NULL)";
            cmd.ExecuteNonQuery();
        }
        public void CreateTableWithColumns(string schema, string table, List<(string Name, Type ClrType, bool Nullable, bool IsComputed, string? JsonPath, bool IsIndexed)> columns)
        {
            var sch = schema ?? (searchPath ?? "public");
            using var cmd = conn.CreateCommand();
            var defs = new System.Text.StringBuilder();
            for (int i = 0; i < columns.Count; i++)
            {
                var col = columns[i];
                if (i > 0) defs.Append(", ");
                if (string.Equals(col.Name, "Id", StringComparison.OrdinalIgnoreCase))
                {
                    defs.Append($"\"{Qual(col.Name)}\" text PRIMARY KEY");
                    continue;
                }
                if (string.Equals(col.Name, "Json", StringComparison.OrdinalIgnoreCase))
                {
                    defs.Append($"\"{Qual(col.Name)}\" jsonb NOT NULL");
                    continue;
                }
                if (col.IsComputed && !string.IsNullOrEmpty(col.JsonPath))
                {
                    // jsonPath like $.Prop -> extract Prop
                    var prop = col.JsonPath.StartsWith("$.") ? col.JsonPath.Substring(2) : col.JsonPath.TrimStart('$', '.');
                    defs.Append($"\"{Qual(col.Name)}\" text GENERATED ALWAYS AS ((\"Json\"->>'{prop}')) STORED");
                }
                else
                {
                    var pgType = col.ClrType == typeof(int) ? "integer" : col.ClrType == typeof(long) ? "bigint" : col.ClrType == typeof(bool) ? "boolean" : col.ClrType == typeof(DateTime) ? "timestamp with time zone" : "text";
                    var nullSql = col.Nullable ? string.Empty : " NOT NULL";
                    defs.Append($"\"{Qual(col.Name)}\" {pgType}{nullSql}");
                }
            }

            cmd.CommandText = $"CREATE TABLE IF NOT EXISTS \"{Qual(sch)}\".\"{Qual(table)}\" ({defs});";
            try { cmd.ExecuteNonQuery(); } catch { }

            // Create indexes for indexed columns
            for (int i = 0; i < columns.Count; i++)
            {
                var c = columns[i];
                if (c.IsIndexed)
                {
                    var ix = $"IX_{table}_{c.Name}";
                    using var cmd2 = conn.CreateCommand();
                    cmd2.CommandText = $"CREATE INDEX IF NOT EXISTS \"{Qual(ix)}\" ON \"{Qual(sch)}\".\"{Qual(table)}\" (\"{Qual(c.Name)}\");";
                    try { cmd2.ExecuteNonQuery(); } catch { }
                }
            }
        }
        public void AddComputedColumnFromJson(string schema, string table, string column, string jsonPath, bool persisted)
        {
            // Postgres uses generated columns from jsonb extraction
            var sch = schema ?? (searchPath ?? "public");
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"ALTER TABLE \"{Qual(sch)}\".\"{Qual(table)}\" ADD COLUMN IF NOT EXISTS \"{Qual(column)}\" text GENERATED ALWAYS AS ((\"Json\"->>substring(@p from 3))) STORED";
            // @p like $.Prop -> use substring to strip '$.'; simple helper for tests
            var p = conn.CreateCommand();
            cmd.Parameters.AddWithValue("@p", jsonPath);
            cmd.ExecuteNonQuery();
        }
        public void AddPhysicalColumn(string schema, string table, string column, Type clrType, bool nullable)
        {
            var sch = schema ?? (searchPath ?? "public");
            string pgType = clrType == typeof(int) ? "integer" : clrType == typeof(long) ? "bigint" : clrType == typeof(bool) ? "boolean" : clrType == typeof(DateTime) ? "timestamp with time zone" : "text";
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"ALTER TABLE \"{Qual(sch)}\".\"{Qual(table)}\" ADD COLUMN IF NOT EXISTS \"{Qual(column)}\" {pgType} {(nullable ? "" : "NOT NULL")}";
            cmd.ExecuteNonQuery();
        }
        public void CreateIndex(string schema, string table, string indexName, IReadOnlyList<string> columns, bool unique)
        {
            var sch = schema ?? (searchPath ?? "public");
            var cols = string.Join(", ", columns.Select(c => "\"" + Qual(c) + "\""));
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"CREATE {(unique ? "UNIQUE " : string.Empty)}INDEX IF NOT EXISTS \"{Qual(indexName)}\" ON \"{Qual(sch)}\".\"{Qual(table)}\" ({cols})";
            cmd.ExecuteNonQuery();
        }
    }

    private sealed class PostgresStoreFeatures : IRelationalStoreFeatures
    {
        public bool SupportsJsonFunctions => true;
        public bool SupportsPersistedComputedColumns => true; // generated columns are stored
        public bool SupportsIndexesOnComputedColumns => true;
        public string ProviderName => "postgresql";
    }

    private (int offset, int limit) ComputeSkipTake(DataQueryOptions? options)
    {
        var page = options?.Page is int p && p > 0 ? p : 1;
        var sizeReq = options?.PageSize;
        var size = sizeReq is int ps && ps > 0 ? ps : _defaultPageSize;
        if (size > _maxPageSize) size = _maxPageSize;
        var offset = (page - 1) * size;
        return (offset, size);
    }

    private string RewriteEntityToken(string sql)
    {
        var token = typeof(TEntity).Name;
        var physical = QualifiedTable;
        var pattern = $"\\b{System.Text.RegularExpressions.Regex.Escape(token)}\\b";
        return System.Text.RegularExpressions.Regex.Replace(sql, pattern, physical, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private string RewriteWhereForProjection(string whereSql)
    {
        var projections = ProjectionResolver.Get(typeof(TEntity));
        bool hasQuotes = whereSql.IndexOf('"') >= 0;
        if (hasQuotes)
        {
            // Assume already quoted; best-effort replace property names inside quotes
            foreach (var p in projections)
            {
                whereSql = whereSql.Replace("\"" + p.Property.Name + "\"", "\"" + p.ColumnName + "\"");
            }
            return whereSql;
        }

        // Replace bare identifiers with jsonb extraction or projected columns (if any)
        var map = projections.ToDictionary(p => p.Property.Name, p => "\"" + p.ColumnName + "\"", StringComparer.Ordinal);
        whereSql = System.Text.RegularExpressions.Regex.Replace(whereSql, "\n|\r", " ");
        foreach (var kv in map)
        {
            var ident = kv.Key;
            var col = kv.Value;
            whereSql = System.Text.RegularExpressions.Regex.Replace(
                whereSql,
                $"\\b{System.Text.RegularExpressions.Regex.Escape(ident)}\\b",
                col);
        }
        // Fallback: JSONB extraction for unknown tokens
        whereSql = System.Text.RegularExpressions.Regex.Replace(
            whereSql,
            "(?<![@:])\\b([A-Za-z_][A-Za-z0-9_]*)\\b",
            m =>
            {
                var token = m.Groups[1].Value;
                switch (token.ToUpperInvariant())
                {
                    case "AND":
                    case "OR":
                    case "NOT":
                    case "NULL":
                    case "LIKE":
                    case "IN":
                    case "IS":
                    case "BETWEEN":
                    case "EXISTS":
                    case "SELECT":
                    case "FROM":
                    case "WHERE":
                    case "GROUP":
                    case "BY":
                    case "ORDER":
                    case "OFFSET":
                    case "FETCH":
                    case "ASC":
                    case "DESC":
                    case "LIMIT":
                        return token;
                }
                if (map.ContainsKey(token)) return map[token];
                return $"(\"Json\" #>> '{{{token}}}')"; // text extraction
            });
        return whereSql;
    }

    private static List<TEntity> MapRowsToEntities(IEnumerable<dynamic> rows)
    {
        var list = new List<TEntity>();
        foreach (var row in rows)
        {
            var dict = (IDictionary<string, object?>)row;
            if (dict.TryGetValue("Json", out var jsonVal))
            {
                var jsonStr = jsonVal?.ToString();
                if (!string.IsNullOrWhiteSpace(jsonStr))
                {
                    try
                    {
                        var ent = Newtonsoft.Json.JsonConvert.DeserializeObject<TEntity>(jsonStr!);
                        if (ent is not null) list.Add(ent);
                        continue;
                    }
                    catch
                    {
                        // fall through to default instance
                    }
                }
            }
            var ent2 = Activator.CreateInstance<TEntity>();
            list.Add(ent2);
        }
        return list;
    }

    private static TResult CastScalar<TResult>(object? value)
    {
        if (value is null) return default!;
        var t = typeof(TResult);
        if (t.IsAssignableFrom(value.GetType())) return (TResult)value;
        try { return (TResult)Convert.ChangeType(value, t); } catch { return default!; }
    }

    private static IReadOnlyList<Dictionary<string, object?>> MapDynamicRows(IEnumerable<dynamic> rows)
    {
        var list = new List<Dictionary<string, object?>>();
        foreach (var row in rows)
        {
            if (row is IDictionary<string, object?> map)
            {
                list.Add(new Dictionary<string, object?>(map, StringComparer.OrdinalIgnoreCase));
            }
            else
            {
                // Fallback: reflect properties
                var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                var props = ((object)row).GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                foreach (var p in props)
                {
                    if (!p.CanRead) continue;
                    dict[p.Name] = p.GetValue(row);
                }
                list.Add(dict);
            }
        }
        return list;
    }

    private bool IsDdlAllowed(bool entityReadOnly)
    {
        if (_options.DdlPolicy != SchemaDdlPolicy.AutoCreate) return false;
        if (entityReadOnly) return false;
        bool prod = KoanEnv.IsProduction;
        bool allowMagic = KoanEnv.AllowMagicInProduction || _options.AllowProductionDdl;
        try
        {
            var cfg = _sp.GetService(typeof(IConfiguration)) as IConfiguration;
            if (cfg is not null)
            {
                allowMagic = allowMagic || Configuration.Read(cfg, Constants.Configuration.Koan.AllowMagicInProduction, false);
            }
        }
        catch { }
        return !prod || allowMagic;
    }
}
