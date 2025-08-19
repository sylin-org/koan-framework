using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sora.Core;
using Sora.Data.Abstractions;
using Sora.Data.Abstractions.Instructions;
using Sora.Data.Abstractions.Naming;
using Sora.Data.Abstractions.Annotations;
using Sora.Data.Core;
using Sora.Data.Relational.Linq;

namespace Sora.Data.Sqlite;

internal static class SqliteTelemetry
{
    public static readonly System.Diagnostics.ActivitySource Activity = new("Sora.Data.Sqlite");
}

internal sealed class SqliteHealthContributor(IOptions<SqliteOptions> options) : IHealthContributor
{
    public string Name => "data:sqlite";
    public bool IsCritical => true;
    public async Task<HealthReport> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            await using var conn = new SqliteConnection(options.Value.ConnectionString);
            await conn.OpenAsync(ct);
            // trivial probe
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA user_version;";
            _ = await cmd.ExecuteScalarAsync(ct);
            return new HealthReport(Name, HealthState.Healthy);
        }
        catch (Exception ex)
        {
            return new HealthReport(Name, HealthState.Unhealthy, ex.Message, ex);
        }
    }
}

public sealed class SqliteOptions
{
    [Required]
    public string ConnectionString { get; set; } = "Data Source=./data/app.db";
    public StorageNamingStyle NamingStyle { get; set; } = StorageNamingStyle.FullNamespace;
    public string Separator { get; set; } = ".";
    // Paging guardrails (ADR-0044)
    public int DefaultPageSize { get; set; } = 50;
    public int MaxPageSize { get; set; } = 200;
    // Schema policy
    public SchemaDdlPolicy DdlPolicy { get; set; } = SchemaDdlPolicy.AutoCreate; // default per note
    public SchemaMatchingMode SchemaMatching { get; set; } = SchemaMatchingMode.Relaxed; // default per note
    // Global safety: allow DDL in prod only with an explicit magic flag
    public bool AllowProductionDdl { get; set; } = false;
}

public enum SchemaDdlPolicy { NoDdl, Validate, AutoCreate }
public enum SchemaMatchingMode { Relaxed, Strict }

public static class SqliteRegistration
{
    public static IServiceCollection AddSqliteAdapter(this IServiceCollection services, Action<SqliteOptions>? configure = null)
    {
    services.AddOptions<SqliteOptions>().ValidateDataAnnotations();
    services.TryAddEnumerable(ServiceDescriptor.Transient<IConfigureOptions<SqliteOptions>, SqliteOptionsConfigurator>());
        if (configure is not null) services.Configure(configure);
        // Ensure health contributor is available even outside Sora bootstrap
    services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, SqliteHealthContributor>());
        services.AddSingleton<IDataAdapterFactory, SqliteAdapterFactory>();
        return services;
    }
}

// legacy initializer removed in favor of standardized auto-registrar

internal sealed class SqliteOptionsConfigurator(IConfiguration config) : IConfigureOptions<SqliteOptions>
{
    public void Configure(SqliteOptions options)
    {
        options.ConnectionString = Sora.Core.Configuration.ReadFirst(
            config,
            defaultValue: options.ConnectionString,
            Sora.Data.Sqlite.Infrastructure.Constants.Configuration.Keys.ConnectionString,
            Sora.Data.Sqlite.Infrastructure.Constants.Configuration.Keys.AltConnectionString,
            Sora.Data.Sqlite.Infrastructure.Constants.Configuration.Keys.ConnectionStringsSqlite,
            Sora.Data.Sqlite.Infrastructure.Constants.Configuration.Keys.ConnectionStringsDefault);
        // Paging guardrails
        options.DefaultPageSize = Sora.Core.Configuration.ReadFirst(
            config,
            defaultValue: options.DefaultPageSize,
            Sora.Data.Sqlite.Infrastructure.Constants.Configuration.Keys.DefaultPageSize,
            Sora.Data.Sqlite.Infrastructure.Constants.Configuration.Keys.AltDefaultPageSize);
        options.MaxPageSize = Sora.Core.Configuration.ReadFirst(
            config,
            defaultValue: options.MaxPageSize,
            Sora.Data.Sqlite.Infrastructure.Constants.Configuration.Keys.MaxPageSize,
            Sora.Data.Sqlite.Infrastructure.Constants.Configuration.Keys.AltMaxPageSize);
        // Governance
        var ddlStr = Sora.Core.Configuration.ReadFirst(
            config,
            defaultValue: options.DdlPolicy.ToString(),
            Sora.Data.Sqlite.Infrastructure.Constants.Configuration.Keys.DdlPolicy,
            Sora.Data.Sqlite.Infrastructure.Constants.Configuration.Keys.AltDdlPolicy);
        if (!string.IsNullOrWhiteSpace(ddlStr))
        {
            if (Enum.TryParse<SchemaDdlPolicy>(ddlStr, ignoreCase: true, out var ddl)) options.DdlPolicy = ddl;
        }
        var smStr = Sora.Core.Configuration.ReadFirst(
            config,
            defaultValue: options.SchemaMatching.ToString(),
            Sora.Data.Sqlite.Infrastructure.Constants.Configuration.Keys.SchemaMatchingMode,
            Sora.Data.Sqlite.Infrastructure.Constants.Configuration.Keys.AltSchemaMatchingMode);
        if (!string.IsNullOrWhiteSpace(smStr))
        {
            if (Enum.TryParse<SchemaMatchingMode>(smStr, ignoreCase: true, out var sm)) options.SchemaMatching = sm;
        }
        // Magic flag for production overrides
        options.AllowProductionDdl = Sora.Core.Configuration.Read(
            config,
            Sora.Core.Infrastructure.Constants.Configuration.Sora.AllowMagicInProduction,
            options.AllowProductionDdl);
    }
}

[Sora.Data.Abstractions.ProviderPriority(10)]
public sealed class SqliteAdapterFactory : IDataAdapterFactory
{
    public bool CanHandle(string provider) => string.Equals(provider, "sqlite", StringComparison.OrdinalIgnoreCase);

    public IDataRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider sp)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var opts = sp.GetRequiredService<IOptions<SqliteOptions>>().Value;
        var resolver = sp.GetRequiredService<IStorageNameResolver>();
        return new SqliteRepository<TEntity, TKey>(sp, opts, resolver);
    }
}

internal sealed class SqliteRepository<TEntity, TKey> :
    IDataRepository<TEntity, TKey>,
    ILinqQueryRepository<TEntity, TKey>,
    IStringQueryRepository<TEntity, TKey>,
    IDataRepositoryWithOptions<TEntity, TKey>,
    ILinqQueryRepositoryWithOptions<TEntity, TKey>,
    IStringQueryRepositoryWithOptions<TEntity, TKey>,
    IQueryCapabilities,
    IWriteCapabilities,
    IBulkUpsert<TKey>,
    IBulkDelete<TKey>,
    IInstructionExecutor<TEntity>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    public QueryCapabilities Capabilities => QueryCapabilities.Linq | QueryCapabilities.String;
    public WriteCapabilities Writes => WriteCapabilities.BulkUpsert | WriteCapabilities.BulkDelete | WriteCapabilities.AtomicBatch;

    private readonly IServiceProvider _sp;
    private readonly SqliteOptions _options;
    private readonly IStorageNameResolver _nameResolver;
    private readonly StorageNameResolver.Convention _conv;
    private readonly ILinqSqlDialect _dialect = new SqliteDialect();
    private readonly int _defaultPageSize;
    private readonly int _maxPageSize;
    private readonly ILogger _logger;

    public SqliteRepository(IServiceProvider sp, SqliteOptions options, IStorageNameResolver resolver)
    {
        _sp = sp;
        _options = options;
      _nameResolver = resolver;
    // Initialize runtime snapshot so AllowMagicInProduction and environment are honored in tests and apps
    Sora.Core.SoraEnv.TryInitialize(sp);
      // Logger: prefer typed logger; fall back to category
      _logger = (sp.GetService(typeof(ILogger<SqliteRepository<TEntity, TKey>>)) as ILogger)
            ?? (sp.GetService(typeof(ILoggerFactory)) is ILoggerFactory lf
                ? lf.CreateLogger($"Sora.Data.Sqlite[{typeof(TEntity).FullName}]")
                : NullLogger.Instance);
        _conv = new StorageNameResolver.Convention(options.NamingStyle, options.Separator, NameCasing.AsIs);
        _defaultPageSize = options.DefaultPageSize > 0 ? options.DefaultPageSize : 50;
        _maxPageSize = options.MaxPageSize > 0 ? options.MaxPageSize : 200;
    }

    // Use central registry so DataSetContext is honored (set-aware names)
    private string TableName => Sora.Data.Core.Configuration.StorageNameRegistry.GetOrCompute<TEntity, TKey>(_sp);

    private IDbConnection Open()
    {
        var cs = _options.ConnectionString;
        var conn = new SqliteConnection(cs);
        conn.Open();
        EnsureTable(conn);
        return conn;
    }

    private void EnsureTable(SqliteConnection conn)
    {
        // Respect governance: DDL policy, read-only attribute, and production magic flag
        bool entityReadOnly = typeof(TEntity).GetCustomAttributes(typeof(ReadOnlyAttribute), inherit: true).Any();
    bool allowDdl = IsDdlAllowed(entityReadOnly);

        Log.EnsureTableStart(_logger, TableName, _options.DdlPolicy.ToString(), allowDdl, entityReadOnly);

        // If DDL is disallowed, return early if table exists; do not attempt create/alter
        if (!allowDdl)
        {
            if (TableExists(conn)) { Log.EnsureTableEnd(_logger, TableName); return; } // exists, but we won't attempt to alter
            // Table missing and DDL not allowed: no-op; callers will get runtime errors until provisioned out-of-band
            Log.EnsureTableSkip(_logger, TableName, _options.DdlPolicy.ToString(), "DDL not allowed");
            return;
        }

        using var cmd = conn.CreateCommand();
        // Create base table; generated columns will be added below via ALTER TABLE to maximize compatibility
        cmd.CommandText = $@"CREATE TABLE IF NOT EXISTS [{TableName}] (
    Id TEXT PRIMARY KEY,
    Json TEXT NOT NULL DEFAULT '{{}}'
);";
        cmd.ExecuteNonQuery();
    Log.EnsureTableCreated(_logger, TableName);
        // Discover existing columns
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var schema = conn.CreateCommand())
        {
            schema.CommandText = $"PRAGMA table_info([{TableName}]);";
            using var r = schema.ExecuteReader();
            while (r.Read())
            {
                // column name is at index 1
                if (!r.IsDBNull(1)) existing.Add(r.GetString(1));
            }
        }
        void AddGeneratedColumnIfMissing(string colName, string jsonPath)
        {
            if (existing.Contains(colName)) return;
            try
            {
                using var add = conn.CreateCommand();
                add.CommandText = $"ALTER TABLE [{TableName}] ADD COLUMN [{colName}] GENERATED ALWAYS AS (json_extract(Json, '{jsonPath}')) VIRTUAL";
                add.ExecuteNonQuery();
                existing.Add(colName);
                Log.EnsureTableAddColumn(_logger, TableName, colName, jsonPath);
            }
            catch { /* ignore if not supported */ }
        }
        // Create generated columns and indexes for projected properties
        var projections2 = Sora.Data.Core.ProjectionResolver.Get(typeof(TEntity));
        foreach (var p in projections2)
        {
            var colName = p.ColumnName;
            var jsonPath = "$." + p.Property.Name;
            AddGeneratedColumnIfMissing(colName, jsonPath);
            if (p.IsIndexed)
            {
                try
                {
                    using var idx = conn.CreateCommand();
                    idx.CommandText = $"CREATE INDEX IF NOT EXISTS [{TableName}_idx_{p.ColumnName}] ON [{TableName}] ([{p.ColumnName}])";
                    idx.ExecuteNonQuery();
                    Log.EnsureTableAddIndex(_logger, TableName, p.ColumnName);
                }
                catch { /* ignore */ }
            }
        }
        // Legacy convenience columns for compatibility (Meta as JSON text if present)
        var t2 = typeof(TEntity);
        var metaProp2 = t2.GetProperty("Meta", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
        if (metaProp2 is not null)
        {
            AddGeneratedColumnIfMissing("Meta", "$.Meta");
        }

    Log.EnsureTableEnd(_logger, TableName);
    }

    private void EnsureTable(IDbConnection db)
    {
        if (db is SqliteConnection sc)
        {
            EnsureTable(sc);
        }
        else
        {
            // Non-native connection; best-effort governance: only create when DDL allowed
            bool entityReadOnly = typeof(TEntity).GetCustomAttributes(typeof(ReadOnlyAttribute), inherit: true).Any();
            bool allowDdl = IsDdlAllowed(entityReadOnly);
            if (!allowDdl) return;
            using var cmd = db.CreateCommand();
            cmd.CommandText = $@"CREATE TABLE IF NOT EXISTS [{TableName}] (
    Id TEXT PRIMARY KEY,
    Json TEXT NOT NULL
);";
            cmd.ExecuteNonQuery();
        }
    }

    private void TryEnsureTableWithGovernance(SqliteConnection conn)
    {
        try { EnsureTable(conn); } catch { /* ignore */ }
    }

    private bool TableExists(SqliteConnection conn)
    {
        using var check = conn.CreateCommand();
        check.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=@n LIMIT 1";
        var p = check.CreateParameter();
        p.ParameterName = "@n";
        p.Value = TableName;
        check.Parameters.Add(p);
        try
        {
            var obj = check.ExecuteScalar();
            if (obj is long l) return l == 1;
            if (obj is int i) return i == 1;
            return obj is not null;
        }
        catch { return false; }
    }

    // Basic serialization helpers
    private static TEntity FromRow((string Id, string Json) row)
        => System.Text.Json.JsonSerializer.Deserialize<TEntity>(row.Json)!;
    private static (string Id, string Json) ToRow(TEntity e)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(e);
        var id = e.Id!.ToString()!;
        return (id, json);
    }

    public async Task<TEntity?> GetAsync(TKey id, CancellationToken ct = default)
    {
    ct.ThrowIfCancellationRequested();
        using var act = SqliteTelemetry.Activity.StartActivity("sqlite.get");
        act?.SetTag("entity", typeof(TEntity).FullName);
        using var conn = Open();
        var row = await conn.QuerySingleOrDefaultAsync<(string Id, string Json)>($"SELECT Id, Json FROM [{TableName}] WHERE Id = @Id", new { Id = id!.ToString()! });
        return row == default ? null : FromRow(row);
    }

    public async Task<IReadOnlyList<TEntity>> QueryAsync(object? query, CancellationToken ct = default)
    {
    ct.ThrowIfCancellationRequested();
        using var act = SqliteTelemetry.Activity.StartActivity("sqlite.query:all");
        act?.SetTag("entity", typeof(TEntity).FullName);
        using var conn = Open();
        var rows = await conn.QueryAsync<(string Id, string Json)>($"SELECT Id, Json FROM [{TableName}] LIMIT {_defaultPageSize}");
        return rows.Select(FromRow).ToList();
    }

    public async Task<IReadOnlyList<TEntity>> QueryAsync(object? query, DataQueryOptions? options, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var act = SqliteTelemetry.Activity.StartActivity("sqlite.query:all+opts");
        act?.SetTag("entity", typeof(TEntity).FullName);
        var (offset, limit) = ComputeSkipTake(options);
        using var conn = Open();
        var sql = $"SELECT Id, Json FROM [{TableName}] LIMIT {limit} OFFSET {offset}";
        var rows = await conn.QueryAsync<(string Id, string Json)>(sql);
        return rows.Select(FromRow).ToList();
    }

    public async Task<int> CountAsync(object? query, CancellationToken ct = default)
    {
    ct.ThrowIfCancellationRequested();
    using var act = SqliteTelemetry.Activity.StartActivity("sqlite.count:all");
    act?.SetTag("entity", typeof(TEntity).FullName);
        using var conn = Open();
        return await conn.ExecuteScalarAsync<int>($"SELECT COUNT(1) FROM [{TableName}]");
    }

    public async Task<IReadOnlyList<TEntity>> QueryAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken ct = default)
    {
        // Translate predicate to SQL WHERE via relational LINQ translator
        ct.ThrowIfCancellationRequested();
        using var act = SqliteTelemetry.Activity.StartActivity("sqlite.query:linq");
        act?.SetTag("entity", typeof(TEntity).FullName);
        var translator = new LinqWhereTranslator<TEntity>(_dialect);
        try
        {
            var (whereSql, parameters) = translator.Translate(predicate);
            // Replace property references with projected column or JSON extraction when needed
            whereSql = RewriteWhereForProjection(whereSql);
            using var conn = Open();
            var sql = $"SELECT Id, Json FROM [{TableName}] WHERE {whereSql} LIMIT {_defaultPageSize}";
            var dyn = new DynamicParameters();
            for (int i = 0; i < parameters.Count; i++) dyn.Add($"p{i}", parameters[i]);
            try
            {
                var rows = await conn.QueryAsync<(string Id, string Json)>(sql, dyn);
                return rows.Select(FromRow).ToList();
            }
            catch (SqliteException ex) when ((ex.SqliteErrorCode == 1) && ex.Message.Contains("no such column", StringComparison.OrdinalIgnoreCase))
            {
                throw; // columns should be provided via projection or JSON1 rewrite
            }
        }
        catch (NotSupportedException)
        {
            // Safe fallback: in-memory filtering for unsupported shapes
            var all = await QueryAsync((object?)null, ct);
            return all.AsQueryable().Where(predicate).ToList();
        }
    }

    public async Task<IReadOnlyList<TEntity>> QueryAsync(Expression<Func<TEntity, bool>> predicate, DataQueryOptions? options, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var act = SqliteTelemetry.Activity.StartActivity("sqlite.query:linq+opts");
        act?.SetTag("entity", typeof(TEntity).FullName);
        var translator = new LinqWhereTranslator<TEntity>(_dialect);
        try
        {
            var (whereSql, parameters) = translator.Translate(predicate);
            whereSql = RewriteWhereForProjection(whereSql);
            var (offset, limit) = ComputeSkipTake(options);
            using var conn = Open();
            var sql = $"SELECT Id, Json FROM [{TableName}] WHERE {whereSql} LIMIT {limit} OFFSET {offset}";
            var dyn = new DynamicParameters();
            for (int i = 0; i < parameters.Count; i++) dyn.Add($"p{i}", parameters[i]);
            try
            {
                var rows = await conn.QueryAsync<(string Id, string Json)>(sql, dyn);
                return rows.Select(FromRow).ToList();
            }
            catch (SqliteException ex) when ((ex.SqliteErrorCode == 1) && ex.Message.Contains("no such column", StringComparison.OrdinalIgnoreCase))
            {
                throw;
            }
        }
        catch (NotSupportedException)
        {
            var all = await QueryAsync((object?)null, options, ct);
            return all.AsQueryable().Where(predicate).ToList();
        }
    }

    public Task<int> CountAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken ct = default)
    {
    ct.ThrowIfCancellationRequested();
    using var act = SqliteTelemetry.Activity.StartActivity("sqlite.count:linq");
    act?.SetTag("entity", typeof(TEntity).FullName);
        var translator = new LinqWhereTranslator<TEntity>(_dialect);
        try
        {
            var (whereSql, parameters) = translator.Translate(predicate);
            using var conn = Open();
            var sql = $"SELECT COUNT(1) FROM [{TableName}] WHERE {whereSql}";
            var dyn = new DynamicParameters();
            for (int i = 0; i < parameters.Count; i++) dyn.Add($"p{i}", parameters[i]);
            return conn.ExecuteScalarAsync<int>(sql, dyn);
        }
        catch (NotSupportedException)
        {
            // Fallback to materialize + count
            return Task.FromResult(QueryAsync(predicate, ct).Result.Count);
        }
    }

    public async Task<IReadOnlyList<TEntity>> QueryAsync(string sql, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var act = SqliteTelemetry.Activity.StartActivity("sqlite.query:string");
        act?.SetTag("entity", typeof(TEntity).FullName);
        using var conn = Open();
    if (IsFullSelect(sql))
        {
            var rewritten = RewriteEntityToken(sql);
            try
            {
                var rows = await conn.QueryAsync(rewritten);
                return MapRowsToEntities(rows);
            }
            catch (SqliteException ex) when (IsNoSuchTableForEntity(ex))
            {
                Log.RetryMissingTable(_logger, TableName, ex.SqliteErrorCode);
                EnsureTable(conn);
                var rows = await conn.QueryAsync(rewritten);
                return MapRowsToEntities(rows);
            }
        }
    else
        {
            var whereSql = RewriteWhereForProjection(sql);
            var rows = await conn.QueryAsync<(string Id, string Json)>($"SELECT Id, Json FROM [{TableName}] WHERE " + whereSql + $" LIMIT {_defaultPageSize}");
            return rows.Select(FromRow).ToList();
        }
    }

    public async Task<IReadOnlyList<TEntity>> QueryAsync(string sql, object? parameters, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var act = SqliteTelemetry.Activity.StartActivity("sqlite.query:string:param");
        act?.SetTag("entity", typeof(TEntity).FullName);
        using var conn = Open();
    if (IsFullSelect(sql))
        {
            var rewritten = RewriteEntityToken(sql);
            try
            {
                var rows = await conn.QueryAsync(rewritten, parameters);
                return MapRowsToEntities(rows);
            }
            catch (SqliteException ex) when (IsNoSuchTableForEntity(ex))
            {
                Log.RetryMissingTable(_logger, TableName, ex.SqliteErrorCode);
                EnsureTable(conn);
                var rows = await conn.QueryAsync(rewritten, parameters);
                return MapRowsToEntities(rows);
            }
        }
        else
        {
            var whereSql = RewriteWhereForProjection(sql);
            var rows = await conn.QueryAsync<(string Id, string Json)>($"SELECT Id, Json FROM [{TableName}] WHERE " + whereSql + $" LIMIT {_defaultPageSize}", parameters);
            return rows.Select(FromRow).ToList();
        }
    }

    public async Task<IReadOnlyList<TEntity>> QueryAsync(string sql, DataQueryOptions? options, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var act = SqliteTelemetry.Activity.StartActivity("sqlite.query:string+opts");
        act?.SetTag("entity", typeof(TEntity).FullName);
        using var conn = Open();
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
            var rows = await conn.QueryAsync<(string Id, string Json)>($"SELECT Id, Json FROM [{TableName}] WHERE " + whereSql + $" LIMIT {limit} OFFSET {offset}");
            return rows.Select(FromRow).ToList();
        }
    }

    public async Task<IReadOnlyList<TEntity>> QueryAsync(string sql, object? parameters, DataQueryOptions? options, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var act = SqliteTelemetry.Activity.StartActivity("sqlite.query:string:param+opts");
        act?.SetTag("entity", typeof(TEntity).FullName);
        using var conn = Open();
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
            var rows = await conn.QueryAsync<(string Id, string Json)>($"SELECT Id, Json FROM [{TableName}] WHERE " + whereSql + $" LIMIT {limit} OFFSET {offset}", parameters);
            return rows.Select(FromRow).ToList();
        }
    }

    public async Task<int> CountAsync(string sql, CancellationToken ct = default)
    {
    ct.ThrowIfCancellationRequested();
        using var act = SqliteTelemetry.Activity.StartActivity("sqlite.count:string");
        act?.SetTag("entity", typeof(TEntity).FullName);
        using var conn = Open();
    var whereSql = RewriteWhereForProjection(sql);
    return await conn.ExecuteScalarAsync<int>($"SELECT COUNT(1) FROM [{TableName}] WHERE " + whereSql);
    }

    public async Task<int> CountAsync(string sql, object? parameters, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var act = SqliteTelemetry.Activity.StartActivity("sqlite.count:string:param");
        act?.SetTag("entity", typeof(TEntity).FullName);
        using var conn = Open();
    var whereSql = RewriteWhereForProjection(sql);
    return await conn.ExecuteScalarAsync<int>($"SELECT COUNT(1) FROM [{TableName}] WHERE " + whereSql, parameters);
    }

    public async Task<TEntity> UpsertAsync(TEntity model, CancellationToken ct = default)
    {
        await UpsertManyAsync(new[] { model }, ct);
        return model;
    }

    public async Task<bool> DeleteAsync(TKey id, CancellationToken ct = default)
        => await DeleteManyAsync(new[] { id }, ct).ContinueWith(t => t.Result > 0, ct);

    public async Task<int> UpsertManyAsync(IEnumerable<TEntity> models, CancellationToken ct = default)
    {
        using var conn = Open();
        // Best-effort ensure in case the connection factory didn’t create yet
        if (conn is SqliteConnection sc0)
        {
            try { TryEnsureTableWithGovernance(sc0); } catch { }
            // Final guard: if DDL allowed, issue a direct CREATE TABLE IF NOT EXISTS to avoid any timing issues
            try
            {
                bool readOnly = typeof(TEntity).GetCustomAttributes(typeof(ReadOnlyAttribute), inherit: true).Any();
                if (IsDdlAllowed(readOnly))
                {
                    using var pre = sc0.CreateCommand();
                    pre.CommandText = $"CREATE TABLE IF NOT EXISTS [{TableName}] (Id TEXT PRIMARY KEY, Json TEXT NOT NULL DEFAULT '{{}}')";
                    pre.ExecuteNonQuery();
                }
            }
            catch { /* ignore */ }
        }
    using var tx = conn.BeginTransaction();
        var count = 0;
        foreach (var e in models)
        {
            ct.ThrowIfCancellationRequested();
            var row = ToRow(e);
            try
            {
                await conn.ExecuteAsync($"INSERT INTO [{TableName}] (Id, Json) VALUES (@Id, @Json) ON CONFLICT(Id) DO UPDATE SET Json = excluded.Json;", new { row.Id, row.Json }, tx);
            }
        catch (SqliteException ex) when (IsNoSuchTable(ex))
            {
                // Table may not exist yet due to governance gating; ensure then retry once
                if (conn is SqliteConnection sc)
                {
            Log.RetryMissingTable(_logger, TableName, ex.SqliteErrorCode);
                    TryEnsureTableWithGovernance(sc);
                }
                await conn.ExecuteAsync($"INSERT INTO [{TableName}] (Id, Json) VALUES (@Id, @Json) ON CONFLICT(Id) DO UPDATE SET Json = excluded.Json;", new { row.Id, row.Json }, tx);
            }
            count++;
        }
        tx.Commit();
        return count;
    }

    public async Task<int> DeleteManyAsync(IEnumerable<TKey> ids, CancellationToken ct = default)
    {
        using var conn = Open();
        var count = await conn.ExecuteAsync($"DELETE FROM [{TableName}] WHERE Id IN @Ids", new { Ids = ids.Select(i => i!.ToString()!).ToArray() });
        return count;
    }

    public async Task<int> DeleteAllAsync(CancellationToken ct = default)
    {
        using var conn = Open();
        return await conn.ExecuteAsync($"DELETE FROM [{TableName}]");
    }

    public IBatchSet<TEntity, TKey> CreateBatch() => new SqliteBatch(this);

    private sealed class SqliteBatch(SqliteRepository<TEntity, TKey> repo) : IBatchSet<TEntity, TKey>
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
            // Apply mutations by loading entities then queueing as updates
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

            // Atomic path: single transaction
            using var conn = repo.Open();
            using var tx = conn.BeginTransaction();
            try
            {
                foreach (var e in upserts)
                {
                    ct.ThrowIfCancellationRequested();
                    var row = ToRow(e);
                    await conn.ExecuteAsync($"INSERT INTO [{repo.TableName}] (Id, Json) VALUES (@Id, @Json) ON CONFLICT(Id) DO UPDATE SET Json = excluded.Json;", new { row.Id, row.Json }, tx);
                }
                var deleted = 0;
                if (_deletes.Any())
                {
                    deleted = await conn.ExecuteAsync($"DELETE FROM [{repo.TableName}] WHERE Id IN @Ids", new { Ids = _deletes.Select(i => i!.ToString()!).ToArray() }, tx);
                }
                tx.Commit();
                return new BatchResult(added, updated, deleted);
            }
            catch
            {
                try { tx.Rollback(); } catch { }
                throw;
            }
        }
    }

    // Minimal SQLite dialect for LINQ translation
    private sealed class SqliteDialect : ILinqSqlDialect
    {
        public string QuoteIdent(string ident) => $"[{ident}]";
        public string EscapeLike(string fragment)
            => fragment.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        public string Parameter(int index) => $"@p{index}";
    }

    // IInstructionExecutor implementation for schema and raw SQL helpers
    public async Task<TResult> ExecuteAsync<TResult>(Instruction instruction, CancellationToken ct = default)
    {
    using var act = SqliteTelemetry.Activity.StartActivity("sqlite.instruction");
        act?.SetTag("entity", typeof(TEntity).FullName);
        using var conn = new SqliteConnection(_options.ConnectionString);
        await conn.OpenAsync(ct);
        switch (instruction.Name)
        {
            case "relational.schema.validate":
            {
                var report = ValidateSchema(conn);
                return (TResult)(object)report;
            }
            case "data.ensureCreated":
                TryEnsureTableWithGovernance(conn);
                object d_ok = true;
                return (TResult)d_ok;
            case "data.clear":
                TryEnsureTableWithGovernance(conn);
                var del = await conn.ExecuteAsync($"DELETE FROM [{TableName}]");
                object d_res = del;
                return (TResult)d_res;
            case "relational.schema.ensureCreated":
                TryEnsureTableWithGovernance(conn);
                object ok = true;
                return (TResult)ok;
            case "relational.sql.scalar":
            {
        var sql = RewriteEntityToken(GetSqlFromInstruction(instruction));
                var p = GetParamsFromInstruction(instruction);
                try
                {
                    var result = await conn.ExecuteScalarAsync(sql, p);
                    return CastScalar<TResult>(result);
                }
                catch (SqliteException ex) when (IsNoSuchTableForEntity(ex))
                {
                    Log.RetryMissingTable(_logger, TableName, ex.SqliteErrorCode);
                    TryEnsureTableWithGovernance(conn);
                    var result = await conn.ExecuteScalarAsync(sql, p);
                    return CastScalar<TResult>(result);
                }
            }
            case "relational.sql.nonquery":
            {
                var sql = RewriteEntityToken(GetSqlFromInstruction(instruction));
                sql = MaybeRewriteInsertForProjection(sql);
                // Ensure table exists if targeting this entity table
                try { TryEnsureTableWithGovernance(conn); } catch { }
                var p = GetParamsFromInstruction(instruction);
                try
                {
                    var affected = await conn.ExecuteAsync(sql, p);
                    object res = affected;
                    return (TResult)res;
                }
                catch (SqliteException ex) when (IsNoSuchTableForEntity(ex))
                {
                    Log.RetryMissingTable(_logger, TableName, ex.SqliteErrorCode);
                    TryEnsureTableWithGovernance(conn);
                    var affected = await conn.ExecuteAsync(sql, p);
                    object res = affected;
                    return (TResult)res;
                }
            }
            default:
                throw new NotSupportedException($"Instruction '{instruction.Name}' not supported by SQLite adapter for {typeof(TEntity).Name}.");
        }
    }

    private object ValidateSchema(SqliteConnection conn)
    {
        // Compute DDL allowance like EnsureTable
    bool entityReadOnly = typeof(TEntity).GetCustomAttributes(typeof(ReadOnlyAttribute), inherit: true).Any();
    bool ddlAllowed = IsDdlAllowed(entityReadOnly);

    var table = TableName;
    Log.ValidateSchemaStart(_logger, table);
        var exists = TableExists(conn);
        var projections = Sora.Data.Core.ProjectionResolver.Get(typeof(TEntity));
        var projectedColumns = projections.Select(p => p.ColumnName).Distinct(StringComparer.Ordinal).ToArray();
        var missing = new List<string>();
        if (exists)
        {
            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var schema = conn.CreateCommand();
            // Use table_xinfo to include hidden/generated columns in the result set
            schema.CommandText = $"PRAGMA table_xinfo([{table}]);";
            using var r = schema.ExecuteReader();
            while (r.Read()) { if (!r.IsDBNull(1)) existing.Add(r.GetString(1)); }
            foreach (var col in projectedColumns)
                if (!existing.Contains(col)) missing.Add(col);
        }

        // Determine state per SchemaMatchingMode (resolve fresh to respect latest configuration)
        var mode = _options.SchemaMatching;
        try
        {
            var cfg = _sp.GetService(typeof(Microsoft.Extensions.Configuration.IConfiguration)) as Microsoft.Extensions.Configuration.IConfiguration;
            var smStr = Sora.Core.Configuration.ReadFirst(cfg, _options.SchemaMatching.ToString(),
                Sora.Data.Sqlite.Infrastructure.Constants.Configuration.Keys.SchemaMatchingMode,
                Sora.Data.Sqlite.Infrastructure.Constants.Configuration.Keys.AltSchemaMatchingMode);
            if (!string.IsNullOrWhiteSpace(smStr) && Enum.TryParse<SchemaMatchingMode>(smStr, true, out var parsed))
                mode = parsed;
            else if (_sp.GetService(typeof(Microsoft.Extensions.Options.IOptions<SqliteOptions>)) is Microsoft.Extensions.Options.IOptions<SqliteOptions> opt)
                mode = opt.Value.SchemaMatching;
        }
        catch { /* best effort */ }
        string state;
        if (!exists)
            state = mode == SchemaMatchingMode.Strict ? "Unhealthy" : "Degraded";
        else if (missing.Count > 0)
            state = mode == SchemaMatchingMode.Strict ? "Unhealthy" : "Degraded";
        else
            state = "Healthy";

    var dict = new Dictionary<string, object?>
        {
            ["Provider"] = "sqlite",
            ["Table"] = table,
            ["TableExists"] = exists,
            ["ProjectedColumns"] = projectedColumns,
            ["MissingColumns"] = missing.ToArray(),
            ["Policy"] = _options.DdlPolicy.ToString(),
            ["DdlAllowed"] = ddlAllowed,
            ["MatchingMode"] = mode.ToString(),
            ["State"] = state
        };
    Log.ValidateSchemaResult(_logger, table, exists, missing.Count, _options.DdlPolicy.ToString(), ddlAllowed, mode.ToString(), state);
        return dict;
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

    private (int offset, int limit) ComputeSkipTake(DataQueryOptions? options)
    {
        // Default page index is 1-based
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
        var physical = TableName;
        // Use simple word-boundary replace; bracket the physical name
        // Case-insensitive replacement
        var pattern = $"\\b{System.Text.RegularExpressions.Regex.Escape(token)}\\b";
        return System.Text.RegularExpressions.Regex.Replace(sql, pattern, $"[{physical}]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private bool IsNoSuchTableForEntity(SqliteException ex)
    {
        if (ex.SqliteErrorCode != 1) return false; // generic SQL error; 1 often maps to no such table
        var name = TableName;
        var msg = ex.Message ?? string.Empty;
        // Relax: treat any "no such table" as retriable; we still ensure our table and retry once
        return msg.IndexOf("no such table", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private bool IsDdlAllowed(bool entityReadOnly)
    {
        if (_options.DdlPolicy != SchemaDdlPolicy.AutoCreate) return false;
        if (entityReadOnly) return false;
        bool prod = Sora.Core.SoraEnv.IsProduction;
        bool allowMagic = Sora.Core.SoraEnv.AllowMagicInProduction || _options.AllowProductionDdl;
        try
        {
            var cfg = _sp.GetService(typeof(IConfiguration)) as IConfiguration;
            if (cfg is not null)
            {
                allowMagic = allowMagic || Sora.Core.Configuration.Read(cfg, Sora.Core.Infrastructure.Constants.Configuration.Sora.AllowMagicInProduction, false);
            }
        }
        catch { /* best effort */ }
        return !prod || allowMagic;
    }

    private static bool IsNoSuchTable(SqliteException ex)
    {
        if (ex.SqliteErrorCode != 1) return false;
        var msg = ex.Message ?? string.Empty;
        return msg.IndexOf("no such table", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static List<TEntity> MapRowsToEntities(IEnumerable<dynamic> rows)
    {
        var list = new List<TEntity>();
        var t = typeof(TEntity);
        var idProp = t.GetProperty("Id");
        foreach (var row in rows)
        {
            var dict = (IDictionary<string, object?>)row;
            // Prefer Json column if present
            if (dict.TryGetValue("Json", out var jsonVal) && jsonVal is string jsonStr && !string.IsNullOrWhiteSpace(jsonStr))
            {
                var ent = System.Text.Json.JsonSerializer.Deserialize<TEntity>(jsonStr);
                if (ent is not null) list.Add(ent);
                continue;
            }
            // otherwise map known columns
            var ent2 = Activator.CreateInstance<TEntity>();
            if (idProp is not null && dict.TryGetValue("Id", out var idv) && idv is not null)
            {
                // TKey may not be string; attempt change type if needed
                try { idProp.SetValue(ent2, (TKey)Convert.ChangeType(idv, typeof(TKey))); }
                catch { idProp.SetValue(ent2, idv); }
            }
            var titleProp = t.GetProperty("Title", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
            if (titleProp is not null && dict.TryGetValue("Title", out var tv))
            {
                try { titleProp.SetValue(ent2, tv?.ToString()); } catch { }
            }
            var metaProp = t.GetProperty("Meta", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
            if (metaProp is not null && dict.TryGetValue("Meta", out var mv))
            {
                try
                {
                    if (mv is string ms && !string.IsNullOrWhiteSpace(ms) && ms.TrimStart().StartsWith("{"))
                    {
                        var obj = System.Text.Json.JsonSerializer.Deserialize(ms, metaProp.PropertyType);
                        if (obj is not null) metaProp.SetValue(ent2, obj);
                    }
                }
                catch { /* ignore */ }
            }
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

    // Projection-aware rewrite: if identifiers are already quoted ([Prop]), only rewrite bracketed tokens.
    // If unquoted, replace bare words with projected columns or JSON1 extraction.
    private string RewriteWhereForProjection(string whereSql)
    {
        var projections = Sora.Data.Core.ProjectionResolver.Get(typeof(TEntity));
        bool hasBrackets = whereSql.IndexOf('[', StringComparison.Ordinal) >= 0;
        if (hasBrackets)
        {
            return System.Text.RegularExpressions.Regex.Replace(
                whereSql,
                "\\[(?<prop>[A-Za-z_][A-Za-z0-9_]*)\\]",
                m =>
                {
                    var prop = m.Groups["prop"].Value;
                    var proj = projections.FirstOrDefault(p => string.Equals(p.ColumnName, prop, StringComparison.Ordinal) || string.Equals(p.Property.Name, prop, StringComparison.Ordinal));
                    if (proj is not null) return $"[{proj.ColumnName}]";
                    return $"json_extract(Json, '$.{prop}')";
                });
        }

        // No brackets: rewrite bare identifiers
        // Map of property name -> projected column
        var map = projections.ToDictionary(p => p.Property.Name, p => $"[{p.ColumnName}]", StringComparer.Ordinal);
        // Replace bare words
        whereSql = System.Text.RegularExpressions.Regex.Replace(
            whereSql,
            "\n|\r",
            " ");
        foreach (var kv in map)
        {
            var ident = kv.Key;
            var col = kv.Value;
            whereSql = System.Text.RegularExpressions.Regex.Replace(
                whereSql,
                $"\\b{System.Text.RegularExpressions.Regex.Escape(ident)}\\b",
                col);
        }
        // Replace any remaining likely props (simple identifiers) with JSON extraction when they are not SQL keywords/operators/parameters
        whereSql = System.Text.RegularExpressions.Regex.Replace(
            whereSql,
            "(?<![@:])\b([A-Za-z_][A-Za-z0-9_]*)\b",
            m =>
            {
                var token = m.Groups[1].Value;
                // Leave common SQL keywords/operators untouched
                switch (token.ToUpperInvariant())
                {
                    case "AND": case "OR": case "NOT": case "NULL": case "LIKE": case "IN": case "IS": case "BETWEEN": case "EXISTS":
                    case "SELECT": case "FROM": case "WHERE": case "GROUP": case "BY": case "ORDER": case "LIMIT": case "OFFSET": case "ASC": case "DESC":
                        return token;
                }
                // If it already got mapped, skip
                if (map.ContainsKey(token)) return map[token];
                return $"json_extract(Json, '$.{token}')";
            });
        return whereSql;
    }

    // If user tries to INSERT into projected columns, rewrite to insert into Json via json_set
    private string MaybeRewriteInsertForProjection(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return sql;
        var s = sql.TrimStart();
        if (!s.StartsWith("insert into", StringComparison.OrdinalIgnoreCase)) return sql;
        // Match: INSERT INTO [Table] (col1, col2, ...) VALUES (v1, v2, ...)
        var rx = new System.Text.RegularExpressions.Regex(
            @"^\s*insert\s+into\s+(?<table>\[[^\]]+\]|[A-Za-z_][A-Za-z0-9_\.]*?)\s*\((?<cols>[^\)]+)\)\s*values\s*\((?<vals>[^\)]+)\)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
        var m = rx.Match(sql);
        if (!m.Success) return sql;
        var table = m.Groups["table"].Value.Trim();
        // Only rewrite if targeting this entity's table (after RewriteEntityToken it should be [Physical])
        var physical = $"[{TableName}]";
        if (!string.Equals(table, physical, StringComparison.Ordinal)) return sql;

        var cols = m.Groups["cols"].Value.Split(',').Select(c => c.Trim()).ToArray();
        var vals = m.Groups["vals"].Value.Split(',').Select(v => v.Trim()).ToArray();
        if (cols.Length != vals.Length) return sql;

        // Collect Id value and JSON setters for others
        string? idExpr = null;
        var jsonArgs = new List<string>();
        for (int i = 0; i < cols.Length; i++)
        {
            var col = cols[i].Trim();
            if (col.StartsWith("[")) col = col.Trim('[', ']');
            var val = vals[i].Trim();
            if (string.Equals(col, "Id", StringComparison.Ordinal))
            {
                idExpr = val;
            }
            else if (string.Equals(col, "Json", StringComparison.Ordinal))
            {
                // Start json with provided value
                jsonArgs.Add($"({val})");
            }
            else
            {
                // Set $.Col = val
                jsonArgs.Add($"'$.{col}'");
                jsonArgs.Add(val);
            }
        }
        if (idExpr is null) return sql; // don't handle

        // Build json_set expression
        string jsonExpr;
        if (jsonArgs.Count == 0)
        {
            jsonExpr = "'{}'";
        }
        else
        {
            string baseExpr = jsonArgs.Count > 0 && jsonArgs[0].StartsWith("(") ? jsonArgs[0] : "'{}'";
            var setters = jsonArgs[0].StartsWith("(") ? jsonArgs.Skip(1) : jsonArgs.AsEnumerable();
            jsonExpr = $"json_set({baseExpr}, {string.Join(", ", setters)})";
        }

        var rewritten = $"INSERT INTO {physical} (Id, Json) VALUES ({idExpr}, {jsonExpr})";
        return rewritten;
    }
}

// Structured logging helpers for SQLite adapter (reusable, allocation-free)
internal static class Log
{
    private static readonly Action<ILogger, string, string, bool, bool, Exception?> _ensureTableStart
        = LoggerMessage.Define<string, string, bool, bool>(LogLevel.Debug, new EventId(1000, "sqlite.ensure_table.start"),
            "Ensure table start: table={Table} policy={Policy} ddlAllowed={DdlAllowed} readOnly={ReadOnly}");
    private static readonly Action<ILogger, string, Exception?> _ensureTableCreated
        = LoggerMessage.Define<string>(LogLevel.Information, new EventId(1001, "sqlite.ensure_table.created"),
            "Table created (or exists): table={Table}");
    private static readonly Action<ILogger, string, string, Exception?> _ensureTableSkip
        = LoggerMessage.Define<string, string>(LogLevel.Debug, new EventId(1002, "sqlite.ensure_table.skip"),
            "Ensure table skipped: table={Table} reason={Reason}");
    private static readonly Action<ILogger, string, string, string, Exception?> _ensureTableAddColumn
        = LoggerMessage.Define<string, string, string>(LogLevel.Debug, new EventId(1003, "sqlite.ensure_table.add_column"),
            "Added generated column: table={Table} column={Column} path={JsonPath}");
    private static readonly Action<ILogger, string, string, Exception?> _ensureTableAddIndex
        = LoggerMessage.Define<string, string>(LogLevel.Debug, new EventId(1004, "sqlite.ensure_table.add_index"),
            "Ensured index: table={Table} column={Column}");
    private static readonly Action<ILogger, string, Exception?> _ensureTableEnd
        = LoggerMessage.Define<string>(LogLevel.Debug, new EventId(1005, "sqlite.ensure_table.end"),
            "Ensure table end: table={Table}");

    private static readonly Action<ILogger, string, Exception?> _validateSchemaStart
        = LoggerMessage.Define<string>(LogLevel.Debug, new EventId(1100, "sqlite.validate_schema.start"),
            "Validate schema start: table={Table}");
    private static readonly Action<ILogger, string, string, Exception?> _validateSchemaResultInfo
        = LoggerMessage.Define<string, string>(LogLevel.Information, new EventId(1101, "sqlite.validate_schema.result"),
            "Validate schema result: table={Table} state={State}");
    private static readonly Action<ILogger, string, string, Exception?> _validateSchemaResultWarn
        = LoggerMessage.Define<string, string>(LogLevel.Warning, new EventId(1102, "sqlite.validate_schema.result_unhealthy"),
            "Validate schema result: table={Table} state={State}");

    private static readonly Action<ILogger, string, int, Exception?> _retryMissingTable
        = LoggerMessage.Define<string, int>(LogLevel.Information, new EventId(1200, "sqlite.retry_missing_table"),
            "Retry after ensuring missing table: table={Table} code={Code}");

    public static void EnsureTableStart(ILogger logger, string table, string policy, bool ddlAllowed, bool readOnly)
        => _ensureTableStart(logger, table, policy, ddlAllowed, readOnly, null);
    public static void EnsureTableCreated(ILogger logger, string table)
        => _ensureTableCreated(logger, table, null);
    public static void EnsureTableSkip(ILogger logger, string table, string policy, string reason)
        => _ensureTableSkip(logger, table, reason, null);
    public static void EnsureTableAddColumn(ILogger logger, string table, string column, string jsonPath)
        => _ensureTableAddColumn(logger, table, column, jsonPath, null);
    public static void EnsureTableAddIndex(ILogger logger, string table, string column)
        => _ensureTableAddIndex(logger, table, column, null);
    public static void EnsureTableEnd(ILogger logger, string table)
        => _ensureTableEnd(logger, table, null);

    public static void ValidateSchemaStart(ILogger logger, string table)
        => _validateSchemaStart(logger, table, null);
    public static void ValidateSchemaResult(ILogger logger, string table, bool exists, int missing, string policy, bool ddlAllowed, string matching, string state)
    {
        if (string.Equals(state, "Healthy", StringComparison.OrdinalIgnoreCase))
        {
            _validateSchemaResultInfo(logger, table, state, null);
            logger.LogDebug("Validate schema details: table={Table} exists={Exists} missing={Missing} policy={Policy} ddlAllowed={DdlAllowed} matching={Matching}",
                table, exists, missing, policy, ddlAllowed, matching);
        }
        else
        {
            _validateSchemaResultWarn(logger, table, state, null);
            logger.LogDebug("Validate schema details: table={Table} exists={Exists} missing={Missing} policy={Policy} ddlAllowed={DdlAllowed} matching={Matching}",
                table, exists, missing, policy, ddlAllowed, matching);
        }
    }

    public static void RetryMissingTable(ILogger logger, string table, int code)
        => _retryMissingTable(logger, table, code, null);
}

internal sealed class SqliteNamingDefaultsProvider : INamingDefaultsProvider
{
    public string Provider => "sqlite";
    public StorageNameResolver.Convention GetConvention(IServiceProvider services)
    {
        var opts = services.GetRequiredService<IOptions<SqliteOptions>>().Value;
        return new StorageNameResolver.Convention(opts.NamingStyle, opts.Separator, NameCasing.AsIs);
    }
    public Func<Type, string?>? GetAdapterOverride(IServiceProvider services) => null;
}
