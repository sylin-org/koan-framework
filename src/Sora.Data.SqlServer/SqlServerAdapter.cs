using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sora.Core;
using Sora.Data.Abstractions;
using Sora.Data.Abstractions.Annotations;
using Sora.Data.Abstractions.Instructions;
using Sora.Data.Abstractions.Naming;
using Sora.Data.Core;
using Sora.Data.Relational.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Sora.Data.SqlServer;

internal static class SqlServerTelemetry
{
    public static readonly System.Diagnostics.ActivitySource Activity = new("Sora.Data.SqlServer");
}

public sealed class SqlServerOptions
{
    [Required]
    public string ConnectionString { get; set; } = "Server=localhost;Database=sora;User Id=sa;Password=Your_password123;TrustServerCertificate=True";
    public StorageNamingStyle NamingStyle { get; set; } = StorageNamingStyle.FullNamespace;
    public string Separator { get; set; } = ".";
    public int DefaultPageSize { get; set; } = 50;
    public int MaxPageSize { get; set; } = 200;
    // Governance
    public SchemaDdlPolicy DdlPolicy { get; set; } = SchemaDdlPolicy.AutoCreate;
    public SchemaMatchingMode SchemaMatching { get; set; } = SchemaMatchingMode.Relaxed;
    public bool AllowProductionDdl { get; set; } = false;
}

public enum SchemaDdlPolicy { NoDdl, Validate, AutoCreate }
public enum SchemaMatchingMode { Relaxed, Strict }

public static class SqlServerRegistration
{
    public static IServiceCollection AddSqlServerAdapter(this IServiceCollection services, Action<SqlServerOptions>? configure = null)
    {
        services.AddOptions<SqlServerOptions>().ValidateDataAnnotations();
        services.TryAddEnumerable(ServiceDescriptor.Transient<IConfigureOptions<SqlServerOptions>, SqlServerOptionsConfigurator>());
        if (configure is not null) services.Configure(configure);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, SqlServerHealthContributor>());
        services.AddSingleton<IDataAdapterFactory, SqlServerAdapterFactory>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Sora.Data.Core.Configuration.IDataProviderConnectionFactory, SqlServerConnectionFactory>());
        return services;
    }
}

internal sealed class SqlServerOptionsConfigurator(IConfiguration config) : IConfigureOptions<SqlServerOptions>
{
    public void Configure(SqlServerOptions options)
    {
        options.ConnectionString = Sora.Core.Configuration.ReadFirst(
            config,
            defaultValue: options.ConnectionString,
            Sora.Data.SqlServer.Infrastructure.Constants.Configuration.Keys.ConnectionString,
            Sora.Data.SqlServer.Infrastructure.Constants.Configuration.Keys.AltConnectionString,
            Sora.Data.SqlServer.Infrastructure.Constants.Configuration.Keys.ConnectionStringsSqlServer,
            Sora.Data.SqlServer.Infrastructure.Constants.Configuration.Keys.ConnectionStringsDefault);

        options.DefaultPageSize = Sora.Core.Configuration.ReadFirst(
            config,
            defaultValue: options.DefaultPageSize,
            Sora.Data.SqlServer.Infrastructure.Constants.Configuration.Keys.DefaultPageSize,
            Sora.Data.SqlServer.Infrastructure.Constants.Configuration.Keys.AltDefaultPageSize);
        options.MaxPageSize = Sora.Core.Configuration.ReadFirst(
            config,
            defaultValue: options.MaxPageSize,
            Sora.Data.SqlServer.Infrastructure.Constants.Configuration.Keys.MaxPageSize,
            Sora.Data.SqlServer.Infrastructure.Constants.Configuration.Keys.AltMaxPageSize);

        var ddlStr = Sora.Core.Configuration.ReadFirst(config, options.DdlPolicy.ToString(),
            Sora.Data.SqlServer.Infrastructure.Constants.Configuration.Keys.DdlPolicy,
            Sora.Data.SqlServer.Infrastructure.Constants.Configuration.Keys.AltDdlPolicy);
        if (!string.IsNullOrWhiteSpace(ddlStr) && Enum.TryParse<SchemaDdlPolicy>(ddlStr, true, out var ddl)) options.DdlPolicy = ddl;

        var smStr = Sora.Core.Configuration.ReadFirst(config, options.SchemaMatching.ToString(),
            Sora.Data.SqlServer.Infrastructure.Constants.Configuration.Keys.SchemaMatchingMode,
            Sora.Data.SqlServer.Infrastructure.Constants.Configuration.Keys.AltSchemaMatchingMode);
        if (!string.IsNullOrWhiteSpace(smStr) && Enum.TryParse<SchemaMatchingMode>(smStr, true, out var sm)) options.SchemaMatching = sm;

        options.AllowProductionDdl = Sora.Core.Configuration.Read(
            config,
            Sora.Core.Infrastructure.Constants.Configuration.Sora.AllowMagicInProduction,
            options.AllowProductionDdl);
    }
}

internal sealed class SqlServerHealthContributor(IOptions<SqlServerOptions> options) : IHealthContributor
{
    public string Name => "data:sqlserver";
    public bool IsCritical => true;
    public async Task<HealthReport> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            await using var conn = new SqlConnection(options.Value.ConnectionString);
            await conn.OpenAsync(ct);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1";
            _ = await cmd.ExecuteScalarAsync(ct);
            return new HealthReport(Name, HealthState.Healthy);
        }
        catch (Exception ex)
        {
            return new HealthReport(Name, HealthState.Unhealthy, ex.Message, ex);
        }
    }
}

[Sora.Data.Abstractions.ProviderPriority(15)]
public sealed class SqlServerAdapterFactory : IDataAdapterFactory
{
    public bool CanHandle(string provider)
        => string.Equals(provider, "mssql", StringComparison.OrdinalIgnoreCase)
        || string.Equals(provider, "sqlserver", StringComparison.OrdinalIgnoreCase)
        || string.Equals(provider, "microsoft.sqlserver", StringComparison.OrdinalIgnoreCase);

    public IDataRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider sp)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var opts = sp.GetRequiredService<IOptions<SqlServerOptions>>().Value;
        var resolver = sp.GetRequiredService<IStorageNameResolver>();
        return new SqlServerRepository<TEntity, TKey>(sp, opts, resolver);
    }
}

internal sealed class SqlServerConnectionFactory : Sora.Data.Core.Configuration.IDataProviderConnectionFactory
{
    public bool CanHandle(string provider)
        => provider.Equals("sqlserver", StringComparison.OrdinalIgnoreCase)
           || provider.Equals("mssql", StringComparison.OrdinalIgnoreCase)
           || provider.Equals("microsoft.sqlserver", StringComparison.OrdinalIgnoreCase);

    public DbConnection Create(string connectionString)
        => new SqlConnection(connectionString);
}

internal sealed class SqlServerRepository<TEntity, TKey> :
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
    private readonly SqlServerOptions _options;
    private readonly IStorageNameResolver _nameResolver;
    private readonly StorageNameResolver.Convention _conv;
    private readonly ILinqSqlDialect _dialect = new MsSqlDialect();
    private readonly int _defaultPageSize;
    private readonly int _maxPageSize;
    private readonly ILogger _logger;

    public SqlServerRepository(IServiceProvider sp, SqlServerOptions options, IStorageNameResolver resolver)
    {
        _sp = sp;
        _options = options;
        _nameResolver = resolver;
        SoraEnv.TryInitialize(sp);
        _logger = (sp.GetService(typeof(ILogger<SqlServerRepository<TEntity, TKey>>)) as ILogger)
            ?? (sp.GetService(typeof(ILoggerFactory)) is ILoggerFactory lf
                ? lf.CreateLogger($"Sora.Data.SqlServer[{typeof(TEntity).FullName}]")
                : NullLogger.Instance);
        _conv = new StorageNameResolver.Convention(options.NamingStyle, options.Separator, NameCasing.AsIs);
        _defaultPageSize = options.DefaultPageSize > 0 ? options.DefaultPageSize : 50;
        _maxPageSize = options.MaxPageSize > 0 ? options.MaxPageSize : 200;
    }

    private string TableName => Sora.Data.Core.Configuration.StorageNameRegistry.GetOrCompute<TEntity, TKey>(_sp);

    private SqlConnection Open()
    {
        var conn = new SqlConnection(_options.ConnectionString);
        conn.Open();
        EnsureTable(conn);
        return conn;
    }

    private void EnsureTable(SqlConnection conn)
    {
        bool entityReadOnly = typeof(TEntity).GetCustomAttributes(typeof(ReadOnlyAttribute), inherit: true).Any();
        bool allowDdl = IsDdlAllowed(entityReadOnly);
        if (!allowDdl)
        {
            if (TableExists(conn)) return;
            return;
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"IF OBJECT_ID(N'[dbo].[{TableName}]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[{TableName}] (
        [Id] NVARCHAR(128) NOT NULL CONSTRAINT PK_{TableName}_Id PRIMARY KEY,
        [Json] NVARCHAR(MAX) NOT NULL
    );
END";
        cmd.ExecuteNonQuery();

        var projections = ProjectionResolver.Get(typeof(TEntity));
        foreach (var p in projections)
        {
            var col = p.ColumnName;
            var path = "$." + p.Property.Name;
            EnsureComputedColumn(conn, col, path);
            if (p.IsIndexed) TryCreateIndex(conn, col);
        }
        var metaProp = typeof(TEntity).GetProperty("Meta", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
        if (metaProp is not null) EnsureComputedColumn(conn, "Meta", "$.Meta");
    }

    private void EnsureComputedColumn(SqlConnection conn, string column, string jsonPath)
    {
        if (ColumnExists(conn, column)) return;
        try
        {
            using var add = conn.CreateCommand();
            add.CommandText = $"ALTER TABLE [dbo].[{TableName}] ADD [{column}] AS JSON_VALUE([Json], '{jsonPath}')";
            add.ExecuteNonQuery();
        }
        catch { }
    }

    private void TryCreateIndex(SqlConnection conn, string column)
    {
        try
        {
            using var idx = conn.CreateCommand();
            idx.CommandText = $@"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_{TableName}_{column}' AND object_id = OBJECT_ID(N'[dbo].[{TableName}]'))
CREATE INDEX [IX_{TableName}_{column}] ON [dbo].[{TableName}] ([{column}]);";
            idx.ExecuteNonQuery();
        }
        catch { }
    }

    private bool TableExists(SqlConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sys.tables WHERE name = @n AND SCHEMA_NAME(schema_id) = 'dbo'";
        cmd.Parameters.Add(new SqlParameter("@n", TableName));
        try { var o = cmd.ExecuteScalar(); return o != null; } catch { return false; }
    }

    private bool ColumnExists(SqlConnection conn, string column)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT 1 FROM sys.columns c
JOIN sys.tables t ON c.object_id = t.object_id
JOIN sys.schemas s ON t.schema_id = s.schema_id
WHERE t.name = @t AND s.name = 'dbo' AND c.name = @c";
        cmd.Parameters.Add(new SqlParameter("@t", TableName));
        cmd.Parameters.Add(new SqlParameter("@c", column));
        try { var o = cmd.ExecuteScalar(); return o != null; } catch { return false; }
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
        using var act = SqlServerTelemetry.Activity.StartActivity("mssql.get");
        act?.SetTag("entity", typeof(TEntity).FullName);
        await using var conn = Open();
        var row = await conn.QuerySingleOrDefaultAsync<(string Id, string Json)>($"SELECT [Id], [Json] FROM [dbo].[{TableName}] WHERE [Id] = @Id", new { Id = id!.ToString()! });
        return row == default ? null : FromRow(row);
    }

    public async Task<IReadOnlyList<TEntity>> QueryAsync(object? query, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var act = SqlServerTelemetry.Activity.StartActivity("mssql.query:all");
        act?.SetTag("entity", typeof(TEntity).FullName);
        await using var conn = Open();
        var rows = await conn.QueryAsync<(string Id, string Json)>($"SELECT [Id], [Json] FROM [dbo].[{TableName}] ORDER BY [Id] OFFSET 0 ROWS FETCH NEXT {_defaultPageSize} ROWS ONLY");
        return rows.Select(FromRow).ToList();
    }

    public async Task<IReadOnlyList<TEntity>> QueryAsync(object? query, DataQueryOptions? options, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var act = SqlServerTelemetry.Activity.StartActivity("mssql.query:all+opts");
        act?.SetTag("entity", typeof(TEntity).FullName);
        var (offset, limit) = ComputeSkipTake(options);
        await using var conn = Open();
        var sql = $"SELECT [Id], [Json] FROM [dbo].[{TableName}] ORDER BY [Id] OFFSET {offset} ROWS FETCH NEXT {limit} ROWS ONLY";
        var rows = await conn.QueryAsync<(string Id, string Json)>(sql);
        return rows.Select(FromRow).ToList();
    }

    public async Task<int> CountAsync(object? query, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var act = SqlServerTelemetry.Activity.StartActivity("mssql.count:all");
        act?.SetTag("entity", typeof(TEntity).FullName);
        await using var conn = Open();
        return await conn.ExecuteScalarAsync<int>($"SELECT COUNT(1) FROM [dbo].[{TableName}]");
    }

    public async Task<IReadOnlyList<TEntity>> QueryAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var act = SqlServerTelemetry.Activity.StartActivity("mssql.query:linq");
        act?.SetTag("entity", typeof(TEntity).FullName);
        var translator = new LinqWhereTranslator<TEntity>(_dialect);
        try
        {
            var (whereSql, parameters) = translator.Translate(predicate);
            whereSql = RewriteWhereForProjection(whereSql);
            await using var conn = Open();
            var sql = $"SELECT [Id], [Json] FROM [dbo].[{TableName}] WHERE {whereSql} ORDER BY [Id] OFFSET 0 ROWS FETCH NEXT {_defaultPageSize} ROWS ONLY";
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
        using var act = SqlServerTelemetry.Activity.StartActivity("mssql.query:linq+opts");
        act?.SetTag("entity", typeof(TEntity).FullName);
        var translator = new LinqWhereTranslator<TEntity>(_dialect);
        try
        {
            var (whereSql, parameters) = translator.Translate(predicate);
            whereSql = RewriteWhereForProjection(whereSql);
            var (offset, limit) = ComputeSkipTake(options);
            await using var conn = Open();
            var sql = $"SELECT [Id], [Json] FROM [dbo].[{TableName}] WHERE {whereSql} ORDER BY [Id] OFFSET {offset} ROWS FETCH NEXT {limit} ROWS ONLY";
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
        using var act = SqlServerTelemetry.Activity.StartActivity("mssql.count:linq");
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
        var sql = $"SELECT COUNT(1) FROM [dbo].[{TableName}] WHERE {whereSql}";
        var dyn = new DynamicParameters();
        for (int i = 0; i < parameters.Count; i++) dyn.Add($"p{i}", parameters[i]);
        return await conn.ExecuteScalarAsync<int>(sql, dyn);
    }

    public async Task<IReadOnlyList<TEntity>> QueryAsync(string sql, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var act = SqlServerTelemetry.Activity.StartActivity("mssql.query:string");
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
            var rows = await conn.QueryAsync<(string Id, string Json)>($"SELECT [Id], [Json] FROM [dbo].[{TableName}] WHERE " + whereSql + $" ORDER BY [Id] OFFSET 0 ROWS FETCH NEXT {_defaultPageSize} ROWS ONLY");
            return rows.Select(FromRow).ToList();
        }
    }

    public async Task<IReadOnlyList<TEntity>> QueryAsync(string sql, object? parameters, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var act = SqlServerTelemetry.Activity.StartActivity("mssql.query:string:param");
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
            var rows = await conn.QueryAsync<(string Id, string Json)>($"SELECT [Id], [Json] FROM [dbo].[{TableName}] WHERE " + whereSql + $" ORDER BY [Id] OFFSET 0 ROWS FETCH NEXT {_defaultPageSize} ROWS ONLY", parameters);
            return rows.Select(FromRow).ToList();
        }
    }

    public async Task<IReadOnlyList<TEntity>> QueryAsync(string sql, DataQueryOptions? options, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var act = SqlServerTelemetry.Activity.StartActivity("mssql.query:string+opts");
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
            var rows = await conn.QueryAsync<(string Id, string Json)>($"SELECT [Id], [Json] FROM [dbo].[{TableName}] WHERE " + whereSql + $" ORDER BY [Id] OFFSET {offset} ROWS FETCH NEXT {limit} ROWS ONLY");
            return rows.Select(FromRow).ToList();
        }
    }

    public async Task<IReadOnlyList<TEntity>> QueryAsync(string sql, object? parameters, DataQueryOptions? options, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var act = SqlServerTelemetry.Activity.StartActivity("mssql.query:string:param+opts");
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
            var rows = await conn.QueryAsync<(string Id, string Json)>($"SELECT [Id], [Json] FROM [dbo].[{TableName}] WHERE " + whereSql + $" ORDER BY [Id] OFFSET {offset} ROWS FETCH NEXT {limit} ROWS ONLY", parameters);
            return rows.Select(FromRow).ToList();
        }
    }

    public async Task<int> CountAsync(string sql, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var act = SqlServerTelemetry.Activity.StartActivity("mssql.count:string");
        act?.SetTag("entity", typeof(TEntity).FullName);
        await using var conn = Open();
        var whereSql = RewriteWhereForProjection(sql);
        return await conn.ExecuteScalarAsync<int>($"SELECT COUNT(1) FROM [dbo].[{TableName}] WHERE " + whereSql);
    }

    public async Task<int> CountAsync(string sql, object? parameters, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var act = SqlServerTelemetry.Activity.StartActivity("mssql.count:string:param");
        act?.SetTag("entity", typeof(TEntity).FullName);
        await using var conn = Open();
        var whereSql = RewriteWhereForProjection(sql);
        return await conn.ExecuteScalarAsync<int>($"SELECT COUNT(1) FROM [dbo].[{TableName}] WHERE " + whereSql, parameters);
    }

    public async Task<TEntity> UpsertAsync(TEntity model, CancellationToken ct = default)
    { await UpsertManyAsync(new[] { model }, ct); return model; }

    public async Task<bool> DeleteAsync(TKey id, CancellationToken ct = default)
        => await DeleteManyAsync(new[] { id }, ct).ContinueWith(t => t.Result > 0, ct);

    public async Task<int> UpsertManyAsync(IEnumerable<TEntity> models, CancellationToken ct = default)
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

    public async Task<int> DeleteManyAsync(IEnumerable<TKey> ids, CancellationToken ct = default)
    {
        await using var conn = Open();
        var idArr = ids.Select(i => i!.ToString()!).ToArray();
        return await conn.ExecuteAsync($"DELETE FROM [dbo].[{TableName}] WHERE [Id] IN @Ids", new { Ids = idArr });
    }

    public async Task<int> DeleteAllAsync(CancellationToken ct = default)
    {
        await using var conn = Open();
        return await conn.ExecuteAsync($"DELETE FROM [dbo].[{TableName}]");
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
            await using var tx = conn.BeginTransaction();
            try
            {
                foreach (var e in upserts)
                {
                    ct.ThrowIfCancellationRequested();
                    var row = ToRow(e);
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
    }

    public async Task<TResult> ExecuteAsync<TResult>(Instruction instruction, CancellationToken ct = default)
    {
        using var act = SqlServerTelemetry.Activity.StartActivity("mssql.instruction");
        act?.SetTag("entity", typeof(TEntity).FullName);
        await using var conn = new SqlConnection(_options.ConnectionString);
        await conn.OpenAsync(ct);
        switch (instruction.Name)
        {
            case global::Sora.Data.Relational.RelationalInstructions.SchemaValidate:
                {
                    var report = ValidateSchema(conn);
                    return (TResult)report;
                }
            case global::Sora.Data.DataInstructions.EnsureCreated:
            case global::Sora.Data.Relational.RelationalInstructions.SchemaEnsureCreated:
                EnsureTable(conn);
                object ok = true; return (TResult)ok;
            case global::Sora.Data.DataInstructions.Clear:
                {
                    EnsureTable(conn);
                    var del = await conn.ExecuteAsync($"DELETE FROM [dbo].[{TableName}]");
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
                throw new NotSupportedException($"Instruction '{instruction.Name}' not supported by SQL Server adapter for {typeof(TEntity).Name}.");
        }
    }

    private object ValidateSchema(SqlConnection conn)
    {
        bool entityReadOnly = typeof(TEntity).GetCustomAttributes(typeof(ReadOnlyAttribute), inherit: true).Any();
        bool ddlAllowed = IsDdlAllowed(entityReadOnly);
        var table = TableName;
        var exists = TableExists(conn);
        var projections = ProjectionResolver.Get(typeof(TEntity));
        var projectedColumns = projections.Select(p => p.ColumnName).Distinct(StringComparer.Ordinal).ToArray();
        var missing = new List<string>();
        if (exists)
        {
            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT c.name FROM sys.columns c JOIN sys.tables t ON c.object_id = t.object_id JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE t.name = @t AND s.name = 'dbo'";
            cmd.Parameters.Add(new SqlParameter("@t", table));
            using var r = cmd.ExecuteReader();
            while (r.Read()) existing.Add(r.GetString(0));
            foreach (var col in projectedColumns) if (!existing.Contains(col)) missing.Add(col);
        }

        var mode = _options.SchemaMatching;
        string state;
        if (!exists) state = mode == SchemaMatchingMode.Strict ? "Unhealthy" : "Degraded";
        else if (missing.Count > 0) state = mode == SchemaMatchingMode.Strict ? "Unhealthy" : "Degraded";
        else state = "Healthy";
        return new Dictionary<string, object?>
        {
            ["Provider"] = "sqlserver",
            ["Table"] = table,
            ["TableExists"] = exists,
            ["ProjectedColumns"] = projectedColumns,
            ["MissingColumns"] = missing.ToArray(),
            ["Policy"] = _options.DdlPolicy.ToString(),
            ["DdlAllowed"] = ddlAllowed,
            ["MatchingMode"] = mode.ToString(),
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
        var physical = TableName;
        var pattern = $"\\b{System.Text.RegularExpressions.Regex.Escape(token)}\\b";
        return System.Text.RegularExpressions.Regex.Replace(sql, pattern, $"[dbo].[{physical}]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private string RewriteWhereForProjection(string whereSql)
    {
        var projections = ProjectionResolver.Get(typeof(TEntity));
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
                    return $"JSON_VALUE([Json], '$.{prop}')";
                });
        }

        var map = projections.ToDictionary(p => p.Property.Name, p => $"[{p.ColumnName}]", StringComparer.Ordinal);
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
                if (map.ContainsKey(token)) return map[token];
                return $"JSON_VALUE([Json], '$.{token}')";
            });
        return whereSql;
    }

    private static List<TEntity> MapRowsToEntities(IEnumerable<dynamic> rows)
    {
        var list = new List<TEntity>();
        foreach (var row in rows)
        {
            var dict = (IDictionary<string, object?>)row;
            if (dict.TryGetValue("Json", out var jsonVal) && jsonVal is string jsonStr && !string.IsNullOrWhiteSpace(jsonStr))
            {
                var ent = System.Text.Json.JsonSerializer.Deserialize<TEntity>(jsonStr);
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

internal sealed class SqlServerNamingDefaultsProvider : INamingDefaultsProvider
{
    public string Provider => "sqlserver";
    public StorageNameResolver.Convention GetConvention(IServiceProvider services)
    {
        var opts = services.GetRequiredService<IOptions<SqlServerOptions>>().Value;
        return new StorageNameResolver.Convention(opts.NamingStyle, opts.Separator, NameCasing.AsIs);
    }
    public Func<Type, string?>? GetAdapterOverride(IServiceProvider services) => null;
}
