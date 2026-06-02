using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Koan.Core;
using Koan.Core.Infrastructure;
using Koan.Core.Capabilities;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Instructions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Abstractions.Sorting;
using Koan.Data.Core;
using Koan.Data.Core.Optimization;
using Koan.Data.Relational.Linq;
using Koan.Data.Relational.Orchestration;
using System.Collections.Frozen;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Koan.Data.Connector.SqlServer;

internal sealed class SqlServerRepository<TEntity, TKey> :
    IDataRepository<TEntity, TKey>,
    IOptimizedDataRepository<TEntity, TKey>,
    IQueryRepository<TEntity, TKey>,
    IRawQueryRepository<TEntity, TKey>,
    IDescribesCapabilities,
    IBulkUpsert<TKey>,
    IBulkDelete<TKey>,
    IInstructionExecutor<TEntity>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    /// <summary>Operators the SQL Server adapter pushes down (DATA-XXXX). Everything else falls to the in-memory floor.</summary>
    public FilterCapabilities FilterCapabilities => RelationalFilterCapabilities.Default;

    public void Describe(ICapabilities caps) => caps
        .Add(DataCaps.Query.Linq).Add(DataCaps.Query.String)
        .Add(DataCaps.Write.BulkUpsert).Add(DataCaps.Write.BulkDelete)
        .Add(DataCaps.Write.AtomicBatch).Add(DataCaps.Write.FastRemove);

    // Storage optimization support
    private readonly StorageOptimizationInfo _optimizationInfo;
    public StorageOptimizationInfo OptimizationInfo => _optimizationInfo;

    private readonly IServiceProvider _sp;
    private readonly SqlServerOptions _options;
    private readonly IStorageNameResolver _nameResolver;
    private readonly StorageNameResolver.Convention _conv;
    private readonly ILinqSqlDialect _dialect = new MsSqlDialect();
    private readonly int _defaultPageSize;
    private readonly ILogger _logger;
    private readonly JsonSerializerSettings _json;
    private static readonly CamelCaseNamingStrategy CamelCase = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _healthyCache = new(StringComparer.Ordinal);

    public SqlServerRepository(IServiceProvider sp, SqlServerOptions options, IStorageNameResolver resolver)
    {
        _sp = sp;
        _options = options;
        _nameResolver = resolver;

        // Get storage optimization info from AggregateBag
        _optimizationInfo = sp.GetStorageOptimization<TEntity, TKey>();

        KoanEnv.TryInitialize(sp);
        _logger = (sp.GetService(typeof(ILogger<SqlServerRepository<TEntity, TKey>>)) as ILogger)
                  ?? (sp.GetService(typeof(ILoggerFactory)) is ILoggerFactory lf
                      ? lf.CreateLogger($"Koan.Data.Connector.SqlServer[{typeof(TEntity).FullName}]")
                      : NullLogger.Instance);
        _conv = new StorageNameResolver.Convention(options.NamingStyle, options.Separator, NameCasing.AsIs);
        _defaultPageSize = options.DefaultPageSize > 0 ? options.DefaultPageSize : 50;
        _json = new JsonSerializerSettings
        {
            ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver
            {
                NamingStrategy = new Newtonsoft.Json.Serialization.CamelCaseNamingStrategy()
            },
            Formatting = options.JsonWriteIndented ? Formatting.Indented : Formatting.None,
            NullValueHandling = options.JsonIgnoreNullValues ? NullValueHandling.Ignore : NullValueHandling.Include
        };

        // Log optimization strategy for diagnostics
        if (_optimizationInfo.IsOptimized)
        {
            _logger.LogInformation("SQL Server Repository Optimization: Entity={EntityType}, OptimizationType={OptimizationType}",
                typeof(TEntity).Name, _optimizationInfo.OptimizationType);
        }
    }

    private string TableName => Core.Configuration.AdapterNaming.GetOrCompute<TEntity, TKey>(_sp);

    /// <summary>
    /// Applies storage optimization to entity before writing to SQL Server.
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
                // For SQL Server, convert GUID string to UNIQUEIDENTIFIER for efficient storage
                if (Guid.TryParse(stringId, out var guid))
                {
                    // SQL Server UNIQUEIDENTIFIER is efficient for storage and indexing
                    idProperty.SetValue(entity, guid.ToString("D")); // Ensure standard format
                }
                break;

            // Future optimization types would go here
        }
    }

    private static string BuildCacheKey(SqlConnection conn, string table)
        => $"{conn.DataSource}/{conn.Database}::{table}";

    private SqlConnection Open()
    {
        var conn = new SqlConnection(_options.ConnectionString);
        conn.Open();
        // IMPORTANT: Do not create or ensure schema here.
        // Schema lifecycle (validation/creation) must be managed by the shared orchestrator
        // via instructions (relational.schema.ensurecreated / data.ensureCreated).
        EnsureOrchestrated(conn);
        return conn;
    }

    private void EnsureOrchestrated(SqlConnection conn)
    {
        var table = TableName;
        var cacheKey = BuildCacheKey(conn, table);
        try
        {
            EnsureOrchestrated(conn, cacheKey, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch
        {
            // best effort: do not fail repository open if orchestration isn't available; let operations surface errors
        }
    }

    private Task EnsureOrchestrated(SqlConnection conn, string cacheKey, CancellationToken ct)
    {
        if (_healthyCache.TryGetValue(cacheKey, out var healthy) && healthy)
        {
            return Task.CompletedTask;
        }

        return Singleflight.Run(cacheKey, async runCt =>
        {
            if (_healthyCache.TryGetValue(cacheKey, out var cached) && cached)
            {
                return;
            }

            try
            {
                var orch = (IRelationalSchemaOrchestrator)_sp.GetRequiredService(typeof(IRelationalSchemaOrchestrator));
                var ddl = new MsSqlDdlExecutor(conn);
                var feats = new MsSqlStoreFeatures();
                var report = (IDictionary<string, object?>)await orch.ValidateAsync<TEntity, TKey>(ddl, feats, runCt);
                var ddlAllowed = report.TryGetValue("DdlAllowed", out var da) && da is bool allowed && allowed;
                var tableExists = report.TryGetValue("TableExists", out var te) && te is bool exists && exists;
                if (ddlAllowed)
                {
                    await orch.EnsureCreatedAsync<TEntity, TKey>(ddl, feats, runCt);
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
                _logger.LogDebug(ex, "SQL Server schema ensure failed for {Table}", TableName);
                _healthyCache.TryRemove(cacheKey, out _);
                throw;
            }
        }, ct);
    }

    public async Task EnsureReady(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await using var conn = new SqlConnection(_options.ConnectionString);
        await conn.OpenAsync(ct);
        var cacheKey = BuildCacheKey(conn, TableName);
        try
        {
            await EnsureOrchestrated(conn, cacheKey, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SQL Server ensure ready failed for {Table}", TableName);
            throw;
        }
    }

    private static string OrderByIdClause => "ORDER BY TRY_CONVERT(BIGINT, [Id]) ASC, [Id] ASC";

    // Schema creation (table + computed projection columns + indexes) is owned exclusively by the
    // IRelationalSchemaOrchestrator async path (see EnsureOrchestrated / ExecuteAsync's EnsureCreated
    // case). The legacy synchronous EnsureTable/EnsureComputedColumn/TryCreateIndex helpers that used
    // to live here fired DDL via *un-awaited* ExecuteNonQueryAsync()/ExecuteScalarAsync() — disposing
    // the command mid-flight, racing commands on a non-MARS connection, and (because an un-awaited Task
    // is never null) reporting every column as already-present so computed columns were never created.
    // They were dead (nothing called them) and have been removed. The only metadata probe still needed
    // is TableExists, used by the Clear/SchemaClear instructions to honour "don't create on clear".
    private static async Task<bool> TableExistsAsync(SqlConnection conn, string tableName, CancellationToken ct)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sys.tables WHERE name = @n AND SCHEMA_NAME(schema_id) = 'dbo'";
        cmd.Parameters.Add(new SqlParameter("@n", tableName));
        try { return await cmd.ExecuteScalarAsync(ct) is not null; }
        catch { return false; }
    }

    private TEntity FromRow((string Id, string Json) row)
        => JsonConvert.DeserializeObject<TEntity>(row.Json, _json)!;
    private (string Id, string Json) ToRow(TEntity e)
    {
    var json = JsonConvert.SerializeObject(e, _json);
        var id = e.Id!.ToString()!;
        return (id, json);
    }

    public async Task<TEntity?> Get(TKey id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var act = SqlServerTelemetry.Activity.StartActivity("mssql.get");
        act?.SetTag("entity", typeof(TEntity).FullName);
        await using var conn = Open();
        var row = await conn.QuerySingleOrDefaultAsync<(string Id, string Json)>($"SELECT [Id], [Json] FROM [dbo].[{TableName}] WHERE [Id] = @Id", new { Id = id!.ToString()! });
        return row == default ? null : FromRow(row);
    }

    public async Task<IReadOnlyList<TEntity?>> GetMany(IEnumerable<TKey> ids, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var act = SqlServerTelemetry.Activity.StartActivity("mssql.get.many");
        act?.SetTag("entity", typeof(TEntity).FullName);

        var idList = ids as IReadOnlyList<TKey> ?? ids.ToList();
        if (idList.Count == 0)
        {
            return [];
        }

        await using var conn = Open();
        var stringIds = idList.Select(id => id!.ToString()!).ToArray();

        // Use IN clause for bulk query
        var rows = await conn.QueryAsync<(string Id, string Json)>(
            $"SELECT [Id], [Json] FROM [dbo].[{TableName}] WHERE [Id] IN @Ids",
            new { Ids = stringIds });

        // Build dictionary for O(1) lookup
        var entityMap = rows.Select(FromRow).ToDictionary(e => e.Id);

        // Preserve order and include nulls
        var results = new TEntity?[idList.Count];
        for (var i = 0; i < idList.Count; i++)
        {
            results[i] = entityMap.TryGetValue(idList[i], out var entity) ? entity : null;
        }

        return results;
    }


    // ==================== Unified Query (DATA-XXXX) ====================

    public async Task<RepositoryQueryResult<TEntity>> Query(QueryDefinition query, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var act = SqlServerTelemetry.Activity.StartActivity("mssql.query");
        act?.SetTag("entity", typeof(TEntity).FullName);

        var (whereSql, parameters) = BuildWhere(query.Filter);
        var (orderBy, sortHandled) = BuildOrderBy(query.Sort);

        var sb = new System.Text.StringBuilder();
        sb.Append("SELECT [Id], [Json] FROM [dbo].[").Append(TableName).Append(']');
        if (whereSql is not null) sb.Append(" WHERE ").Append(whereSql);
        sb.Append(' ').Append(orderBy);

        var paginationHandled = false;
        if (query.HasPagination)
        {
            var size = query.EffectivePageSize();
            var offset = (query.EffectivePage() - 1) * size;
            sb.Append(" OFFSET ").Append(offset).Append(" ROWS FETCH NEXT ").Append(size).Append(" ROWS ONLY");
            paginationHandled = true;
        }

        await using var conn = Open();
        var dyn = ToDapper(parameters);
        var rows = await conn.QueryAsync<(string Id, string Json)>(sb.ToString(), dyn);
        var items = rows.Select(FromRow).ToList();

        long? totalCount = paginationHandled
            ? await CountCore(whereSql, parameters, ct)
            : items.Count;

        return new RepositoryQueryResult<TEntity>
        {
            Items = items,
            TotalCount = totalCount,
            IsEstimate = false,
            SortHandled = sortHandled,
            PaginationHandled = paginationHandled,
        };
    }

    public async Task<CountResult> Count(QueryDefinition query, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var act = SqlServerTelemetry.Activity.StartActivity("mssql.count");
        act?.SetTag("entity", typeof(TEntity).FullName);

        var (whereSql, parameters) = BuildWhere(query.Filter);

        // Fast count via sys.partitions when no filter and strategy allows it.
        if (whereSql is null)
        {
            var strategy = query.CountStrategy ?? CountStrategy.Optimized;
            if (strategy == CountStrategy.Fast || strategy == CountStrategy.Optimized)
            {
                try
                {
                    await using var statConn = Open();
                    var estimate = await statConn.ExecuteScalarAsync<long>(
                        @"SELECT SUM(p.rows)
                          FROM sys.partitions p
                          INNER JOIN sys.tables t ON p.object_id = t.object_id
                          INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                          WHERE s.name = 'dbo' AND t.name = @TableName AND p.index_id IN (0,1)",
                        new { TableName });
                    if (estimate >= 0)
                        return CountResult.Estimate(estimate);
                }
                catch
                {
                    // Fall back to exact count
                }
            }
        }

        return CountResult.Exact(await CountCore(whereSql, parameters, ct));
    }

    private async Task<long> CountCore(string? whereSql, IReadOnlyList<object?> parameters, CancellationToken ct)
    {
        await using var conn = Open();
        var sql = whereSql is null
            ? $"SELECT COUNT(1) FROM [dbo].[{TableName}]"
            : $"SELECT COUNT(1) FROM [dbo].[{TableName}] WHERE {whereSql}";
        var dyn = ToDapper(parameters);
        return await conn.ExecuteScalarAsync<long>(sql, dyn);
    }

    /// <summary>Translates the (fully-pushable) filter to a WHERE fragment; null filter -> no WHERE.</summary>
    private (string? whereSql, IReadOnlyList<object?> parameters) BuildWhere(Filter? filter)
    {
        if (filter is null) return (null, Array.Empty<object?>());
        var translator = new SqlFilterTranslator(_dialect, typeof(TEntity), ResolveColumnSql);
        return translator.Translate(filter);
    }

    /// <summary>
    /// Maps a flat property name to its SQL value expression. Scalar fields lower to a projected
    /// column or <c>JSON_VALUE</c>; collection fields lower to the array node via <c>JSON_QUERY</c>
    /// (so the dialect's OPENJSON helpers can iterate it). The entity is serialized camelCase into
    /// <c>[Json]</c>, so JSON paths use the camelCase property name.
    /// </summary>
    private string ResolveColumnSql(FieldPath field, ResolvedField resolved)
    {
        var prop = field.Leaf;
        if (string.Equals(prop, "Id", StringComparison.Ordinal)) return "[Id]";
        if (string.Equals(prop, "Json", StringComparison.Ordinal)) return "[Json]";
        var camel = CamelCase.GetPropertyName(prop, hasSpecifiedName: false);

        if (resolved is not null && resolved.TargetsCollection)
            return $"JSON_QUERY([Json], '$.{camel}')";

        var json = $"JSON_VALUE([Json], '$.{camel}')";
        var projections = ProjectionResolver.Get(typeof(TEntity));
        var proj = projections.FirstOrDefault(p => string.Equals(p.Property.Name, prop, StringComparison.Ordinal));
        return proj is not null ? $"COALESCE([{proj.ColumnName}], {json})" : json;
    }

    /// <summary>Builds an ORDER BY clause from the sort specs; falls back to a stable Id order.</summary>
    private (string orderBy, IReadOnlySet<SortSpec> sortHandled) BuildOrderBy(IReadOnlyList<SortSpec> sort)
    {
        if (sort is null || sort.Count == 0)
            return (OrderByIdClause, RepositoryQueryResult<TEntity>.NoSortHandled);

        var parts = new List<string>(sort.Count);
        foreach (var spec in sort)
        {
            var leaf = spec.Path.Members[spec.Path.Members.Count - 1].Name;
            var col = ResolveColumnSql(FieldPath.Of(leaf), default!);
            parts.Add($"{col} {(spec.Desc ? "DESC" : "ASC")}");
        }
        var orderBy = "ORDER BY " + string.Join(", ", parts);
        return (orderBy, sort.ToFrozenSet());
    }

    private static DynamicParameters ToDapper(IReadOnlyList<object?> parameters)
    {
        var dyn = new DynamicParameters();
        for (int i = 0; i < parameters.Count; i++) dyn.Add($"p{i}", parameters[i]);
        return dyn;
    }

    // ==================== Raw provider query (IRawQueryRepository) ====================

    public async Task<RepositoryQueryResult<TEntity>> QueryRaw(string query, object? parameters, QueryDefinition shaping, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var act = SqlServerTelemetry.Activity.StartActivity("mssql.query:raw");
        act?.SetTag("entity", typeof(TEntity).FullName);
        await using var conn = Open();
        if (IsFullSelect(query))
        {
            var rewritten = RewriteEntityToken(query);
            var rows = await conn.QueryAsync(rewritten, parameters);
            var items = MapRowsToEntities(rows);
            return new RepositoryQueryResult<TEntity> { Items = items };
        }
        else
        {
            var whereSql = RewriteWhereForProjection(query);
            var size = shaping.HasPagination ? shaping.EffectivePageSize() : _defaultPageSize;
            var offset = shaping.HasPagination ? (shaping.EffectivePage() - 1) * size : 0;
            var sql = $"SELECT [Id], [Json] FROM [dbo].[{TableName}] WHERE {whereSql} {OrderByIdClause} OFFSET {offset} ROWS FETCH NEXT {size} ROWS ONLY";
            var rows = await conn.QueryAsync<(string Id, string Json)>(sql, parameters);
            return new RepositoryQueryResult<TEntity>
            {
                Items = rows.Select(FromRow).ToList(),
                PaginationHandled = shaping.HasPagination,
            };
        }
    }

    public async Task<CountResult> CountRaw(string query, object? parameters, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await using var conn = Open();
        var whereSql = RewriteWhereForProjection(query);
        var count = await conn.ExecuteScalarAsync<long>($"SELECT COUNT(1) FROM [dbo].[{TableName}] WHERE {whereSql}", parameters);
        return CountResult.Exact(count);
    }


    public async Task<TEntity> Upsert(TEntity model, CancellationToken ct = default)
    { await UpsertMany(new[] { model }, ct); return model; }

    public async Task<bool> Delete(TKey id, CancellationToken ct = default)
        => await DeleteMany(new[] { id }, ct).ContinueWith(t => t.Result > 0, ct);

    public async Task<int> UpsertMany(IEnumerable<TEntity> models, CancellationToken ct = default)
    {
        await using var conn = Open();
        await using var tx = conn.BeginTransaction();
        var count = 0;
        foreach (var e in models)
        {
            ct.ThrowIfCancellationRequested();
            var row = ToRow(e);
            var updated = await conn.ExecuteAsync($"UPDATE [dbo].[{TableName}] SET [Json] = @Json WHERE [Id] = @Id", new { row.Id, row.Json }, tx);
            if (updated == 0)
            {
                await conn.ExecuteAsync($"INSERT INTO [dbo].[{TableName}] ([Id], [Json]) VALUES (@Id, @Json)", new { row.Id, row.Json }, tx);
            }
            count++;
        }
        await tx.CommitAsync();
        return count;
    }

    public async Task<int> DeleteMany(IEnumerable<TKey> ids, CancellationToken ct = default)
    {
        await using var conn = Open();
        var idArr = ids.Select(i => i!.ToString()!).ToArray();
        return await conn.ExecuteAsync($"DELETE FROM [dbo].[{TableName}] WHERE [Id] IN @Ids", new { Ids = idArr });
    }

    public async Task<int> DeleteAll(CancellationToken ct = default)
    {
        await using var conn = Open();
        return await conn.ExecuteAsync($"DELETE FROM [dbo].[{TableName}]");
    }

    public async Task<long> RemoveAll(RemoveStrategy strategy, CancellationToken ct = default)
    {
        await using var conn = Open();

        // Resolve Optimized strategy based on provider capabilities
        var effectiveStrategy = strategy == RemoveStrategy.Optimized
            ? RemoveStrategy.Fast // this adapter declares write.fastRemove
            : strategy;

        if (effectiveStrategy == RemoveStrategy.Fast)
        {
            // Fast path: TRUNCATE (bypasses hooks, resets identity)
            try
            {
                await conn.ExecuteAsync($"TRUNCATE TABLE [dbo].[{TableName}]", ct);
                return -1; // TRUNCATE doesn't report count
            }
            catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 4712) // Cannot truncate table referenced by FK
            {
                // Foreign key constraint - fall back to DELETE
                // Silently fall through to safe path
            }
        }

        // Safe path: DELETE (fires hooks if registered)
        var countResult = await Count(QueryDefinition.All, ct);
        await conn.ExecuteAsync($"DELETE FROM [dbo].[{TableName}]", ct);
        return countResult.Value;
    }

    public IBatchSet<TEntity, TKey> CreateBatch() => new MsSqlBatch(this);

    private sealed class MsSqlBatch(SqlServerRepository<TEntity, TKey> repo) : IBatchSet<TEntity, TKey>
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

        public async Task<BatchResult> Save(BatchOptions? options = null, CancellationToken ct = default)
        {
            foreach (var (id, mutate) in _mutations)
            {
                var current = await repo.Get(id, ct);
                if (current is not null) { mutate(current); _updates.Add(current); }
            }
            var upserts = _adds.Concat(_updates);
            var added = _adds.Count; var updated = _updates.Count;
            var requireAtomic = options?.RequireAtomic == true;
            if (!requireAtomic)
            {
                if (upserts.Any()) await repo.UpsertMany(upserts, ct);
                var deleted = 0; if (_deletes.Any()) deleted = await repo.DeleteMany(_deletes, ct);
                return new BatchResult(added, updated, deleted);
            }

            await using var conn = repo.Open();
            await using var tx = conn.BeginTransaction();
            try
            {
                foreach (var e in upserts)
                {
                    ct.ThrowIfCancellationRequested();
                    var row = repo.ToRow(e);
                    var updatedRows = await conn.ExecuteAsync($"UPDATE [dbo].[{repo.TableName}] SET [Json] = @Json WHERE [Id] = @Id", new { row.Id, row.Json }, tx);
                    if (updatedRows == 0)
                        await conn.ExecuteAsync($"INSERT INTO [dbo].[{repo.TableName}] ([Id], [Json]) VALUES (@Id, @Json)", new { row.Id, row.Json }, tx);
                }
                var deleted = 0;
                if (_deletes.Any())
                {
                    deleted = await conn.ExecuteAsync($"DELETE FROM [dbo].[{repo.TableName}] WHERE [Id] IN @Ids", new { Ids = _deletes.Select(i => i!.ToString()!).ToArray() }, tx);
                }
                await tx.CommitAsync();
                return new BatchResult(added, updated, deleted);
            }
            catch
            {
                try { await tx.RollbackAsync(); } catch { }
                throw;
            }
        }
    }

    private sealed class MsSqlDialect : ILinqSqlDialect
    {
        public string QuoteIdent(string ident) => $"[{ident}]";
        public string EscapeLike(string fragment)
            => fragment.Replace("[", "[[").Replace("%", "[%]").Replace("_", "[_]");
        public string Parameter(int index) => $"@p{index}";

        // List<string> is stored as a JSON array inside [Json]. columnSql is a JSON_QUERY(...) array node;
        // OPENJSON iterates its elements (value column).
        public string JsonArrayContains(string columnSql, string parameter)
            => $"EXISTS (SELECT 1 FROM OPENJSON({columnSql}) WHERE value = {parameter})";

        public string JsonArrayLength(string columnSql)
            => $"(SELECT COUNT(*) FROM OPENJSON({columnSql}))";
    }

    public async Task<TResult> ExecuteAsync<TResult>(Instruction instruction, CancellationToken ct = default)
    {
        using var act = SqlServerTelemetry.Activity.StartActivity("mssql.instruction");
        act?.SetTag("entity", typeof(TEntity).FullName);
        await using var conn = new SqlConnection(_options.ConnectionString);
        await conn.OpenAsync(ct);
        switch (instruction.Name)
        {
            case RelationalInstructions.SchemaValidate:
                {
                    var orch = (IRelationalSchemaOrchestrator)_sp.GetRequiredService(typeof(IRelationalSchemaOrchestrator));
                    var ddl = new MsSqlDdlExecutor(conn);
                    var feats = new MsSqlStoreFeatures();
                    var report = await orch.ValidateAsync<TEntity, TKey>(ddl, feats, ct);
                    return (TResult)report;
                }
            case DataInstructions.EnsureCreated:
            case RelationalInstructions.SchemaEnsureCreated:
                {
                    var orch = (IRelationalSchemaOrchestrator)_sp.GetRequiredService(typeof(IRelationalSchemaOrchestrator));
                    var ddl = new MsSqlDdlExecutor(conn);
                    var feats = new MsSqlStoreFeatures();
                    // Decide based on attribute > options precedence handled by orchestrator
                    var key = $"{conn.DataSource}/{conn.Database}::{TableName}";
                    await Singleflight.Run(key, async kct =>
                    {
                        await orch.EnsureCreatedAsync<TEntity, TKey>(ddl, feats, kct);
                        _healthyCache[key] = true;
                    }, ct);
                    object ok = true; return (TResult)ok;
                }
            case DataInstructions.Clear:
                {
                    // Do not create the table when clearing; only delete if it exists so we honor DDL policy.
                    if (!await TableExistsAsync(conn, TableName, ct)) { object res0 = 0; return (TResult)res0; }
                    var del = await conn.ExecuteAsync($"DELETE FROM [dbo].[{TableName}]");
                    object res = del; return (TResult)res;
                }
            case RelationalInstructions.SchemaClear:
                {
                    // Schema clear should remove the table when present, but must not create it.
                    if (!await TableExistsAsync(conn, TableName, ct)) { object res0 = 0; return (TResult)res0; }
                    var drop = $"IF OBJECT_ID(N'[dbo].[{TableName}]', N'U') IS NOT NULL DROP TABLE [dbo].[{TableName}];";
                    var affected = await conn.ExecuteAsync(drop);
                    try { var key = $"{conn.DataSource}/{conn.Database}::{TableName}"; _healthyCache.TryRemove(key, out _); } catch { }
                    object res = affected; return (TResult)res;
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
                throw new NotSupportedException($"Instruction '{instruction.Name}' not supported by SQL Server adapter for {typeof(TEntity).Name}.");
        }
    }

    private static string GetSqlFromInstruction(Instruction instruction)
    {
        // Accept SQL from either payload (string or object with Sql/sql property) or parameters ("sql" key)
        var payload = instruction.Payload;
        if (payload is string s && !string.IsNullOrWhiteSpace(s)) return s;
        if (payload is not null)
        {
            var t = payload.GetType();
            var sqlProp = t.GetProperty("Sql") ?? t.GetProperty("sql");
            if (sqlProp?.GetValue(payload) is string ps && !string.IsNullOrWhiteSpace(ps)) return ps;
        }
        if (instruction.Parameters is not null)
        {
            if (instruction.Parameters.TryGetValue("sql", out var pv) || instruction.Parameters.TryGetValue("Sql", out pv))
            {
                if (pv is string pvs && !string.IsNullOrWhiteSpace(pvs)) return pvs;
            }
        }
        throw new ArgumentException("Instruction payload missing Sql.");
    }
    private static object? GetParamsFromInstruction(Instruction instruction)
        => instruction.Parameters is null ? null : new DynamicParameters(new Dictionary<string, object?>(instruction.Parameters));

    private static bool IsFullSelect(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return false;
        var s = sql.TrimStart();
        return s.StartsWith("select ", StringComparison.OrdinalIgnoreCase);
    }

    private string RewriteEntityToken(string sql)
    {
        var token = typeof(TEntity).Name;
        var physical = TableName;
        // If SQL already contains a schema-qualified bracketed identifier, don't rewrite
        if (System.Text.RegularExpressions.Regex.IsMatch(sql, "\\[[^\\]]+\\]\\.\\[[^\\]]+\\]")) return sql;
        var pattern = $"\\b{System.Text.RegularExpressions.Regex.Escape(token)}\\b";
        return System.Text.RegularExpressions.Regex.Replace(sql, pattern, $"[dbo].[{physical}]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private string RewriteWhereForProjection(string whereSql)
    {
        var projections = ProjectionResolver.Get(typeof(TEntity));
        var columnMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Id"] = "[Id]",
            ["Json"] = "[Json]"
        };

        foreach (var projection in projections)
        {
            var column = $"[{projection.ColumnName}]";
            columnMap[projection.Property.Name] = column;
            var camel = CamelCase.GetPropertyName(projection.Property.Name, hasSpecifiedName: false);
            columnMap[camel] = column;
        }

        string BuildJsonAccessor(string token)
        {
            var camel = CamelCase.GetPropertyName(token, hasSpecifiedName: false);
            return $"JSON_VALUE([Json], '$.{camel}')";
        }

        string BuildColumnOrJson(string token)
        {
            if (columnMap.TryGetValue(token, out var column))
            {
                if (string.Equals(token, "Id", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(token, "Json", StringComparison.OrdinalIgnoreCase))
                {
                    return column;
                }

                var json = BuildJsonAccessor(token);
                return $"COALESCE({column}, {json})";
            }

            return BuildJsonAccessor(token);
        }

        bool hasBrackets = whereSql.IndexOf('[', StringComparison.Ordinal) >= 0;
        if (hasBrackets)
        {
            return System.Text.RegularExpressions.Regex.Replace(
                whereSql,
                "\\[(?<prop>[A-Za-z_][A-Za-z0-9_]*)\\]",
                m =>
                {
                    var prop = m.Groups["prop"].Value;
                    return BuildColumnOrJson(prop);
                });
        }

        whereSql = System.Text.RegularExpressions.Regex.Replace(whereSql, "\n|\r", " ");
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
                        return token;
                }

                return BuildColumnOrJson(token);
            });

        return whereSql;
    }

    private List<TEntity> MapRowsToEntities(IEnumerable<dynamic> rows)
    {
        var list = new List<TEntity>();
        foreach (var row in rows)
        {
            var dict = (IDictionary<string, object?>)row;
            if (dict.TryGetValue("Json", out var jsonVal) && jsonVal is string jsonStr && !string.IsNullOrWhiteSpace(jsonStr))
            {
                var ent = JsonConvert.DeserializeObject<TEntity>(jsonStr, _json);
                if (ent is not null) list.Add(ent);
                continue;
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

}
