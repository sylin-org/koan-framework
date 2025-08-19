using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Sora.Core;
using Sora.Data.Abstractions;
using Sora.Data.Abstractions.Instructions;
using Sora.Data.Abstractions.Naming;
using Sora.Data.Core;
using Sora.Data.Relational.Linq;

namespace Sora.Data.Postgres;

internal static class PgTelemetry
{
    public static readonly System.Diagnostics.ActivitySource Activity = new("Sora.Data.Postgres");
}

public sealed class PostgresOptions
{
    [Required]
    public string ConnectionString { get; set; } = "Host=localhost;Port=5432;Database=sora;Username=postgres;Password=postgres";
    public StorageNamingStyle NamingStyle { get; set; } = StorageNamingStyle.FullNamespace;
    public string Separator { get; set; } = ".";
    public string? SearchPath { get; set; } = "public";
    public int DefaultPageSize { get; set; } = 50;
    public int MaxPageSize { get; set; } = 200;
    public SchemaDdlPolicy DdlPolicy { get; set; } = SchemaDdlPolicy.AutoCreate;
    public SchemaMatchingMode SchemaMatching { get; set; } = SchemaMatchingMode.Relaxed;
    public bool AllowProductionDdl { get; set; } = false;
}

public enum SchemaDdlPolicy { NoDdl, Validate, AutoCreate }
public enum SchemaMatchingMode { Relaxed, Strict }

public static class PostgresRegistration
{
    public static IServiceCollection AddPostgresAdapter(this IServiceCollection services, Action<PostgresOptions>? configure = null)
    {
        services.AddOptions<PostgresOptions>().ValidateDataAnnotations();
        services.TryAddEnumerable(ServiceDescriptor.Transient<IConfigureOptions<PostgresOptions>, PostgresOptionsConfigurator>());
        if (configure is not null) services.Configure(configure);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, PostgresHealthContributor>());
        services.AddSingleton<IDataAdapterFactory, PostgresAdapterFactory>();
        services.TryAddEnumerable(new ServiceDescriptor(typeof(INamingDefaultsProvider), typeof(PostgresNamingDefaultsProvider), ServiceLifetime.Singleton));
        services.TryAddSingleton<IStorageNameResolver, DefaultStorageNameResolver>();
        return services;
    }
}

internal sealed class PostgresOptionsConfigurator(IConfiguration config) : IConfigureOptions<PostgresOptions>
{
    public void Configure(PostgresOptions options)
    {
        options.ConnectionString = Sora.Core.Configuration.ReadFirst(
            config,
            defaultValue: options.ConnectionString,
            Infrastructure.Constants.Configuration.Keys.ConnectionString,
            Infrastructure.Constants.Configuration.Keys.AltConnectionString,
            Infrastructure.Constants.Configuration.Keys.ConnectionStringsPostgres,
            Infrastructure.Constants.Configuration.Keys.ConnectionStringsDefault);

        options.DefaultPageSize = Sora.Core.Configuration.ReadFirst(
            config,
            defaultValue: options.DefaultPageSize,
            Infrastructure.Constants.Configuration.Keys.DefaultPageSize,
            Infrastructure.Constants.Configuration.Keys.AltDefaultPageSize);
        options.MaxPageSize = Sora.Core.Configuration.ReadFirst(
            config,
            defaultValue: options.MaxPageSize,
            Infrastructure.Constants.Configuration.Keys.MaxPageSize,
            Infrastructure.Constants.Configuration.Keys.AltMaxPageSize);

        var ddlStr = Sora.Core.Configuration.ReadFirst(config, options.DdlPolicy.ToString(),
            Infrastructure.Constants.Configuration.Keys.DdlPolicy,
            Infrastructure.Constants.Configuration.Keys.AltDdlPolicy);
        if (!string.IsNullOrWhiteSpace(ddlStr) && Enum.TryParse<SchemaDdlPolicy>(ddlStr, true, out var ddl)) options.DdlPolicy = ddl;

        var smStr = Sora.Core.Configuration.ReadFirst(config, options.SchemaMatching.ToString(),
            Infrastructure.Constants.Configuration.Keys.SchemaMatchingMode,
            Infrastructure.Constants.Configuration.Keys.AltSchemaMatchingMode);
        if (!string.IsNullOrWhiteSpace(smStr) && Enum.TryParse<SchemaMatchingMode>(smStr, true, out var sm)) options.SchemaMatching = sm;

        options.AllowProductionDdl = Sora.Core.Configuration.Read(
            config,
            Sora.Core.Infrastructure.Constants.Configuration.Sora.AllowMagicInProduction,
            options.AllowProductionDdl);

        var sp = Sora.Core.Configuration.ReadFirst(config, options.SearchPath ?? "public",
            Infrastructure.Constants.Configuration.Keys.SearchPath);
        options.SearchPath = sp;
    }
}

internal sealed class PostgresHealthContributor(IOptions<PostgresOptions> options) : IHealthContributor
{
    public string Name => "data:postgres";
    public bool IsCritical => true;
    public async Task<HealthReport> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            await using var conn = new NpgsqlConnection(options.Value.ConnectionString);
            await conn.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand("SELECT 1", conn);
            _ = await cmd.ExecuteScalarAsync(ct);
            return new HealthReport(Name, HealthState.Healthy);
        }
        catch (Exception ex)
        {
            return new HealthReport(Name, HealthState.Unhealthy, ex.Message, ex);
        }
    }
}

[Sora.Data.Abstractions.ProviderPriority(14)]
public sealed class PostgresAdapterFactory : IDataAdapterFactory
{
    public bool CanHandle(string provider)
        => string.Equals(provider, "postgres", StringComparison.OrdinalIgnoreCase)
        || string.Equals(provider, "postgresql", StringComparison.OrdinalIgnoreCase)
        || string.Equals(provider, "npgsql", StringComparison.OrdinalIgnoreCase);

    public IDataRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider sp)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var opts = sp.GetRequiredService<IOptions<PostgresOptions>>().Value;
        var resolver = sp.GetRequiredService<IStorageNameResolver>();
        return new PostgresRepository<TEntity, TKey>(sp, opts, resolver);
    }
}

internal sealed class PostgresNamingDefaultsProvider : INamingDefaultsProvider
{
    public string Provider => "postgres";
    public StorageNameResolver.Convention GetConvention(IServiceProvider services)
    {
        var opts = services.GetRequiredService<IOptions<PostgresOptions>>().Value;
        return new StorageNameResolver.Convention(opts.NamingStyle, opts.Separator, NameCasing.AsIs);
    }
    public Func<Type, string?>? GetAdapterOverride(IServiceProvider services) => null;
}

internal sealed class PgDialect : ILinqSqlDialect
{
    public string QuoteIdent(string ident) => "\"" + ident.Replace("\"", "\"\"") + "\"";
    public string EscapeLike(string fragment)
        => fragment.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
    public string Parameter(int index) => "@p" + index;
}

internal sealed class PostgresRepository<TEntity, TKey> :
    IDataRepository<TEntity, TKey>,
    ILinqQueryRepository<TEntity, TKey>,
    IStringQueryRepository<TEntity, TKey>,
    IDataRepositoryWithOptions<TEntity, TKey>,
    ILinqQueryRepositoryWithOptions<TEntity, TKey>,
    IStringQueryRepositoryWithOptions<TEntity, TKey>,
    IQueryCapabilities,
    IWriteCapabilities,
    IBulkDelete<TKey>,
    IInstructionExecutor<TEntity>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    public QueryCapabilities Capabilities => QueryCapabilities.Linq | QueryCapabilities.String;
    public WriteCapabilities Writes => WriteCapabilities.AtomicBatch | WriteCapabilities.BulkDelete; // BulkUpsert later via COPY staging

    private readonly IServiceProvider _sp;
    private readonly PostgresOptions _options;
    private readonly IStorageNameResolver _nameResolver;
    private readonly StorageNameResolver.Convention _conv;
    private readonly ILinqSqlDialect _dialect = new PgDialect();
    private readonly int _defaultPageSize;
    private readonly int _maxPageSize;
    private readonly ILogger _logger;

    public PostgresRepository(IServiceProvider sp, PostgresOptions options, IStorageNameResolver resolver)
    {
        _sp = sp;
        _options = options;
        _nameResolver = resolver;
        SoraEnv.TryInitialize(sp);
        _logger = (sp.GetService(typeof(ILogger<PostgresRepository<TEntity, TKey>>)) as ILogger)
            ?? (sp.GetService(typeof(ILoggerFactory)) is ILoggerFactory lf
                ? lf.CreateLogger($"Sora.Data.Postgres[{typeof(TEntity).FullName}]")
                : NullLogger.Instance);
        _conv = new StorageNameResolver.Convention(options.NamingStyle, options.Separator, NameCasing.AsIs);
        _defaultPageSize = options.DefaultPageSize > 0 ? options.DefaultPageSize : 50;
        _maxPageSize = options.MaxPageSize > 0 ? options.MaxPageSize : 200;
    }

    private string TableName => Sora.Data.Core.Configuration.StorageNameRegistry.GetOrCompute<TEntity, TKey>(_sp);
    private string QualifiedTable => (_options.SearchPath is { Length: > 0 } sp ? $"\"{sp}\"." : string.Empty) + "\"" + TableName.Replace("\"", "\"\"") + "\"";

    private NpgsqlConnection Open()
    {
        var conn = new NpgsqlConnection(_options.ConnectionString);
        conn.Open();
        EnsureTable(conn);
        return conn;
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
        using var act = PgTelemetry.Activity.StartActivity("pg.get");
        act?.SetTag("entity", typeof(TEntity).FullName);
        await using var conn = Open();
        var row = await conn.QuerySingleOrDefaultAsync<(string Id, string Json)>($"SELECT \"Id\", \"Json\"::text FROM {QualifiedTable} WHERE \"Id\" = @Id", new { Id = id!.ToString()! });
        return row == default ? null : FromRow(row);
    }

    public async Task<IReadOnlyList<TEntity>> QueryAsync(object? query, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var act = PgTelemetry.Activity.StartActivity("pg.query:all");
        act?.SetTag("entity", typeof(TEntity).FullName);
        await using var conn = Open();
        var rows = await conn.QueryAsync<(string Id, string Json)>($"SELECT \"Id\", \"Json\"::text FROM {QualifiedTable} ORDER BY \"Id\" LIMIT {_defaultPageSize} OFFSET 0");
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

    public async Task<int> CountAsync(object? query, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var act = PgTelemetry.Activity.StartActivity("pg.count:all");
        act?.SetTag("entity", typeof(TEntity).FullName);
        await using var conn = Open();
        return await conn.ExecuteScalarAsync<int>($"SELECT COUNT(1) FROM {QualifiedTable}");
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
            var sql = $"SELECT \"Id\", \"Json\"::text FROM {QualifiedTable} WHERE {whereSql} ORDER BY \"Id\" LIMIT {_defaultPageSize} OFFSET 0";
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

    public Task<int> CountAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var act = PgTelemetry.Activity.StartActivity("pg.count:linq");
        act?.SetTag("entity", typeof(TEntity).FullName);
        var translator = new LinqWhereTranslator<TEntity>(_dialect);
        try
        {
            var (whereSql, parameters) = translator.Translate(predicate);
            whereSql = RewriteWhereForProjection(whereSql);
            return CountWhereAsync(whereSql, parameters);
        }
        catch (NotSupportedException)
        {
            return Task.FromResult(QueryAsync(predicate, ct).Result.Count);
        }
    }

    private async Task<int> CountWhereAsync(string whereSql, IReadOnlyList<object?> parameters)
    {
        await using var conn = Open();
        var sql = $"SELECT COUNT(1) FROM {QualifiedTable} WHERE {whereSql}";
        var dyn = new DynamicParameters();
        for (int i = 0; i < parameters.Count; i++) dyn.Add($"p{i}", parameters[i]);
        return await conn.ExecuteScalarAsync<int>(sql, dyn);
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

    public async Task<int> CountAsync(string sql, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var act = PgTelemetry.Activity.StartActivity("pg.count:string");
        act?.SetTag("entity", typeof(TEntity).FullName);
        await using var conn = Open();
        var whereSql = RewriteWhereForProjection(sql);
        return await conn.ExecuteScalarAsync<int>($"SELECT COUNT(1) FROM {QualifiedTable} WHERE " + whereSql);
    }

    public async Task<int> CountAsync(string sql, object? parameters, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var act = PgTelemetry.Activity.StartActivity("pg.count:string:param");
        act?.SetTag("entity", typeof(TEntity).FullName);
        await using var conn = Open();
        var whereSql = RewriteWhereForProjection(sql);
        return await conn.ExecuteScalarAsync<int>($"SELECT COUNT(1) FROM {QualifiedTable} WHERE " + whereSql, parameters);
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
            var row = ToRow(e);
            var affected = await conn.ExecuteAsync($"INSERT INTO {QualifiedTable} (\"Id\", \"Json\") VALUES (@Id, CAST(@Json AS jsonb)) ON CONFLICT (\"Id\") DO UPDATE SET \"Json\" = EXCLUDED.\"Json\"", new { row.Id, row.Json }, tx);
            count += affected > 0 ? 1 : 0;
        }
        await tx.CommitAsync(ct);
        return count;
    }

    public async Task<int> DeleteManyAsync(IEnumerable<TKey> ids, CancellationToken ct = default)
    {
        await using var conn = Open();
        var idArr = ids.Select(i => i!.ToString()!).ToArray();
        return await conn.ExecuteAsync($"DELETE FROM {QualifiedTable} WHERE \"Id\" = ANY(@Ids)", new { Ids = idArr });
    }

    public async Task<int> DeleteAllAsync(CancellationToken ct = default)
    {
        await using var conn = Open();
        return await conn.ExecuteAsync($"DELETE FROM {QualifiedTable}");
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
                    var row = ToRow(e);
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
            case global::Sora.Data.Relational.RelationalInstructions.SchemaValidate:
            {
                var report = ValidateSchema(conn);
                return (TResult)(object)report;
            }
            case global::Sora.Data.DataInstructions.EnsureCreated:
            case global::Sora.Data.Relational.RelationalInstructions.SchemaEnsureCreated:
                EnsureTable(conn);
                object ok = true; return (TResult)ok;
            case global::Sora.Data.Relational.RelationalInstructions.SchemaClear:
            case global::Sora.Data.DataInstructions.Clear:
            {
                EnsureTable(conn);
                var del = await conn.ExecuteAsync($"DELETE FROM {QualifiedTable}");
                object res = del; return (TResult)res;
            }
            case global::Sora.Data.Relational.RelationalInstructions.SqlScalar:
            {
                var sql = RewriteEntityToken(GetSqlFromInstruction(instruction));
                var p = GetParamsFromInstruction(instruction);
                var result = await conn.ExecuteScalarAsync(sql, p);
                return CastScalar<TResult>(result);
            }
            case global::Sora.Data.Relational.RelationalInstructions.SqlNonQuery:
            {
                var sql = RewriteEntityToken(GetSqlFromInstruction(instruction));
                var p = GetParamsFromInstruction(instruction);
                var affected = await conn.ExecuteAsync(sql, p);
                object res = affected; return (TResult)res;
            }
            case global::Sora.Data.Relational.RelationalInstructions.SqlQuery:
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
                    case "AND": case "OR": case "NOT": case "NULL": case "LIKE": case "IN": case "IS": case "BETWEEN": case "EXISTS":
                    case "SELECT": case "FROM": case "WHERE": case "GROUP": case "BY": case "ORDER": case "OFFSET": case "FETCH": case "ASC": case "DESC": case "LIMIT":
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
                    var ent = System.Text.Json.JsonSerializer.Deserialize<TEntity>(jsonStr);
                    if (ent is not null) list.Add(ent);
                    continue;
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
        bool prod = SoraEnv.IsProduction;
        bool allowMagic = SoraEnv.AllowMagicInProduction || _options.AllowProductionDdl;
        try
        {
            var cfg = _sp.GetService(typeof(IConfiguration)) as IConfiguration;
            if (cfg is not null)
            {
                allowMagic = allowMagic || Sora.Core.Configuration.Read(cfg, Sora.Core.Infrastructure.Constants.Configuration.Sora.AllowMagicInProduction, false);
            }
        }
        catch { }
        return !prod || allowMagic;
    }
}
