using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sora.Core;
using Sora.Core.Infrastructure;
using Sora.Data.Abstractions;
using Sora.Data.Abstractions.Annotations;
using Sora.Data.Abstractions.Instructions;
using Sora.Data.Abstractions.Naming;
using Sora.Data.Core;
using Sora.Data.Relational.Linq;
using Sora.Data.Relational.Orchestration;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Data.Common;
using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Serialization;

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
    // Object materialization/serialization
    public bool JsonCaseInsensitive { get; set; } = true;
    public bool JsonWriteIndented { get; set; } = false;
    public bool JsonIgnoreNullValues { get; set; } = false;
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
        services.AddRelationalOrchestration();
        // Bridge SQL Server provider options into the relational materialization pipeline
        services.TryAddEnumerable(ServiceDescriptor.Transient<IConfigureOptions<RelationalMaterializationOptions>, SqlServerToRelationalBridgeConfigurator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, SqlServerHealthContributor>());
        services.AddSingleton<IDataAdapterFactory, SqlServerAdapterFactory>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Sora.Data.Core.Configuration.IDataProviderConnectionFactory, SqlServerConnectionFactory>());
        return services;
    }

    // Bridge SQL Server adapter options into the global RelationalMaterializationOptions used by orchestrator
    internal sealed class SqlServerToRelationalBridgeConfigurator(IOptions<SqlServerOptions> sqlOpts, IConfiguration cfg) : IConfigureOptions<RelationalMaterializationOptions>
    {
        public void Configure(RelationalMaterializationOptions options)
        {
            var so = sqlOpts.Value;
            // Map DDL policy
            options.DdlPolicy = so.DdlPolicy switch
            {
                SchemaDdlPolicy.NoDdl => RelationalDdlPolicy.NoDdl,
                SchemaDdlPolicy.Validate => RelationalDdlPolicy.Validate,
                SchemaDdlPolicy.AutoCreate => RelationalDdlPolicy.AutoCreate,
                _ => options.DdlPolicy
            };
            // Use computed projections by default for SQL Server (supports JSON_VALUE)
            options.Materialization = RelationalMaterializationPolicy.ComputedProjections;
            // Map matching mode
            options.SchemaMatching = so.SchemaMatching == SchemaMatchingMode.Strict ? RelationalSchemaMatchingMode.Strict : RelationalSchemaMatchingMode.Relaxed;
            // Allow production DDL only when explicitly allowed or when provider option permits
            var allowMagic = Configuration.Read(cfg, Constants.Configuration.Sora.AllowMagicInProduction, false);
            options.AllowProductionDdl = so.AllowProductionDdl || allowMagic;
        }
    }
}

internal sealed class SqlServerOptionsConfigurator(IConfiguration config) : IConfigureOptions<SqlServerOptions>
{
    public void Configure(SqlServerOptions options)
    {
        options.ConnectionString = Configuration.ReadFirst(
            config,
            defaultValue: options.ConnectionString,
            Infrastructure.Constants.Configuration.Keys.ConnectionString,
            Infrastructure.Constants.Configuration.Keys.AltConnectionString,
            Infrastructure.Constants.Configuration.Keys.ConnectionStringsSqlServer,
            Infrastructure.Constants.Configuration.Keys.ConnectionStringsDefault);

        options.DefaultPageSize = Configuration.ReadFirst(
            config,
            defaultValue: options.DefaultPageSize,
            Infrastructure.Constants.Configuration.Keys.DefaultPageSize,
            Infrastructure.Constants.Configuration.Keys.AltDefaultPageSize);
        options.MaxPageSize = Configuration.ReadFirst(
            config,
            defaultValue: options.MaxPageSize,
            Infrastructure.Constants.Configuration.Keys.MaxPageSize,
            Infrastructure.Constants.Configuration.Keys.AltMaxPageSize);

        var ddlStr = Configuration.ReadFirst(config, options.DdlPolicy.ToString(),
            Infrastructure.Constants.Configuration.Keys.DdlPolicy,
            Infrastructure.Constants.Configuration.Keys.AltDdlPolicy);
        if (!string.IsNullOrWhiteSpace(ddlStr) && Enum.TryParse<SchemaDdlPolicy>(ddlStr, true, out var ddl)) options.DdlPolicy = ddl;

        var smStr = Configuration.ReadFirst(config, options.SchemaMatching.ToString(),
            Infrastructure.Constants.Configuration.Keys.SchemaMatchingMode,
            Infrastructure.Constants.Configuration.Keys.AltSchemaMatchingMode);
        if (!string.IsNullOrWhiteSpace(smStr) && Enum.TryParse<SchemaMatchingMode>(smStr, true, out var sm)) options.SchemaMatching = sm;

        // Serialization/materialization options
        options.JsonCaseInsensitive = Configuration.Read(
            config,
            Infrastructure.Constants.Configuration.Keys.JsonCaseInsensitive,
            options.JsonCaseInsensitive);
        options.JsonWriteIndented = Configuration.Read(
            config,
            Infrastructure.Constants.Configuration.Keys.JsonWriteIndented,
            options.JsonWriteIndented);
        options.JsonIgnoreNullValues = Configuration.Read(
            config,
            Infrastructure.Constants.Configuration.Keys.JsonIgnoreNullValues,
            options.JsonIgnoreNullValues);

        options.AllowProductionDdl = Configuration.Read(
            config,
            Constants.Configuration.Sora.AllowMagicInProduction,
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

[ProviderPriority(15)]
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
    private readonly JsonSerializerOptions _json;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _healthyCache = new(StringComparer.Ordinal);

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
        _json = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = options.JsonCaseInsensitive,
            WriteIndented = options.JsonWriteIndented
        };
        if (options.JsonIgnoreNullValues)
        {
            _json.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        }
    }

    private string TableName => Core.Configuration.StorageNameRegistry.GetOrCompute<TEntity, TKey>(_sp);

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
        var key = $"{conn.DataSource}/{conn.Database}::{table}";
        // Use the shared relational orchestrator to validate and, if allowed, create schema.
        try
        {
            if (_healthyCache.TryGetValue(key, out var healthy) && healthy) return;
            Singleflight.RunAsync(key, async ct =>
            {
                if (_healthyCache.TryGetValue(key, out var healthy2) && healthy2) return;
                var orch = (IRelationalSchemaOrchestrator)_sp.GetRequiredService(typeof(IRelationalSchemaOrchestrator));
                var ddl = new MsSqlDdlExecutor(conn);
                var feats = new MsSqlStoreFeatures();
                var vReport = (IDictionary<string, object?>)await orch.ValidateAsync<TEntity, TKey>(ddl, feats, ct);
                var ddlAllowed = vReport.TryGetValue("DdlAllowed", out var da) && da is bool db && db;
                var tableExists = vReport.TryGetValue("TableExists", out var te) && te is bool tb && tb;
                if (ddlAllowed)
                {
                    await orch.EnsureCreatedAsync<TEntity, TKey>(ddl, feats, ct);
                    _healthyCache[key] = true; return;
                }
                if (tableExists) { _healthyCache[key] = true; }
            }).GetAwaiter().GetResult();
        }
        catch
        {
            // best effort: do not fail repository open if orchestration isn't available; let operations surface errors
        }
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
        var safe = MakeSafeIdentifier(TableName);
        cmd.CommandText = $@"IF OBJECT_ID(N'[dbo].[{TableName}]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[{TableName}] (
    [Id] NVARCHAR(128) NOT NULL CONSTRAINT [PK_{safe}_Id] PRIMARY KEY,
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

    private static string OrderByIdClause => "ORDER BY TRY_CONVERT(BIGINT, [Id]) ASC, [Id] ASC";

    private static string MakeSafeIdentifier(string name)
        => string.IsNullOrEmpty(name) ? name : System.Text.RegularExpressions.Regex.Replace(name, "[^A-Za-z0-9_]+", "_");

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

    private TEntity FromRow((string Id, string Json) row)
        => JsonSerializer.Deserialize<TEntity>(row.Json, _json)!;
    private (string Id, string Json) ToRow(TEntity e)
    {
        var json = JsonSerializer.Serialize(e, _json);
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
        // When no query/options are provided, return all rows (not paginated)
        var rows = await conn.QueryAsync<(string Id, string Json)>($"SELECT [Id], [Json] FROM [dbo].[{TableName}] {OrderByIdClause}");
        return rows.Select(FromRow).ToList();
    }

    public async Task<IReadOnlyList<TEntity>> QueryAsync(object? query, DataQueryOptions? options, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var act = SqlServerTelemetry.Activity.StartActivity("mssql.query:all+opts");
        act?.SetTag("entity", typeof(TEntity).FullName);
        var (offset, limit) = ComputeSkipTake(options);
        await using var conn = Open();
        var sql = $"SELECT [Id], [Json] FROM [dbo].[{TableName}] {OrderByIdClause} OFFSET {offset} ROWS FETCH NEXT {limit} ROWS ONLY";
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
            var sql = $"SELECT [Id], [Json] FROM [dbo].[{TableName}] WHERE {whereSql} {OrderByIdClause} OFFSET 0 ROWS FETCH NEXT {_defaultPageSize} ROWS ONLY";
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
            var sql = $"SELECT [Id], [Json] FROM [dbo].[{TableName}] WHERE {whereSql} {OrderByIdClause} OFFSET {offset} ROWS FETCH NEXT {limit} ROWS ONLY";
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
            var rows = await conn.QueryAsync<(string Id, string Json)>($"SELECT [Id], [Json] FROM [dbo].[{TableName}] WHERE " + whereSql + $" {OrderByIdClause} OFFSET 0 ROWS FETCH NEXT {_defaultPageSize} ROWS ONLY");
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
            var rows = await conn.QueryAsync<(string Id, string Json)>($"SELECT [Id], [Json] FROM [dbo].[{TableName}] WHERE " + whereSql + $" {OrderByIdClause} OFFSET 0 ROWS FETCH NEXT {_defaultPageSize} ROWS ONLY", parameters);
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
            var rows = await conn.QueryAsync<(string Id, string Json)>($"SELECT [Id], [Json] FROM [dbo].[{TableName}] WHERE " + whereSql + $" {OrderByIdClause} OFFSET {offset} ROWS FETCH NEXT {limit} ROWS ONLY");
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
            var rows = await conn.QueryAsync<(string Id, string Json)>($"SELECT [Id], [Json] FROM [dbo].[{TableName}] WHERE " + whereSql + $" {OrderByIdClause} OFFSET {offset} ROWS FETCH NEXT {limit} ROWS ONLY", parameters);
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
                    await Singleflight.RunAsync(key, async kct =>
                    {
                        await orch.EnsureCreatedAsync<TEntity, TKey>(ddl, feats, kct);
                        _healthyCache[key] = true;
                    }, ct);
                    object ok = true; return (TResult)ok;
                }
            case DataInstructions.Clear:
                {
                    // Do not create the table when clearing; only delete if it exists so we honor DDL policy.
                    if (!TableExists(conn)) { object res0 = 0; return (TResult)res0; }
                    var del = await conn.ExecuteAsync($"DELETE FROM [dbo].[{TableName}]");
                    object res = del; return (TResult)res;
                }
            case RelationalInstructions.SchemaClear:
                {
                    // Schema clear should remove the table when present, but must not create it.
                    if (!TableExists(conn)) { object res0 = 0; return (TResult)res0; }
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
        // If SQL already contains a schema-qualified bracketed identifier, don't rewrite
        if (System.Text.RegularExpressions.Regex.IsMatch(sql, "\\[[^\\]]+\\]\\.\\[[^\\]]+\\]")) return sql;
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

    private List<TEntity> MapRowsToEntities(IEnumerable<dynamic> rows)
    {
        var list = new List<TEntity>();
        foreach (var row in rows)
        {
            var dict = (IDictionary<string, object?>)row;
            if (dict.TryGetValue("Json", out var jsonVal) && jsonVal is string jsonStr && !string.IsNullOrWhiteSpace(jsonStr))
            {
                var ent = JsonSerializer.Deserialize<TEntity>(jsonStr, _json);
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
                allowMagic = allowMagic || Configuration.Read(cfg, Constants.Configuration.Sora.AllowMagicInProduction, false);
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

internal sealed class MsSqlStoreFeatures : IRelationalStoreFeatures
{
    public bool SupportsJsonFunctions => true; // JSON_VALUE is available since SQL Server 2016
    public bool SupportsPersistedComputedColumns => true;
    public bool SupportsIndexesOnComputedColumns => true;
}

internal sealed class MsSqlDdlExecutor : IRelationalDdlExecutor
{
    private readonly SqlConnection _conn;
    public MsSqlDdlExecutor(SqlConnection conn) => _conn = conn;

    public bool TableExists(string schema, string table)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id=s.schema_id WHERE t.name=@t AND s.name=@s";
        cmd.Parameters.Add(new SqlParameter("@t", table));
        cmd.Parameters.Add(new SqlParameter("@s", schema));
        try { var o = cmd.ExecuteScalar(); return o != null; } catch { return false; }
    }

    public bool ColumnExists(string schema, string table, string column)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"SELECT 1 FROM sys.columns c
JOIN sys.tables t ON c.object_id = t.object_id
JOIN sys.schemas s ON t.schema_id = s.schema_id
WHERE t.name = @t AND s.name = @s AND c.name = @c";
        cmd.Parameters.Add(new SqlParameter("@t", table));
        cmd.Parameters.Add(new SqlParameter("@s", schema));
        cmd.Parameters.Add(new SqlParameter("@c", column));
        try { var o = cmd.ExecuteScalar(); return o != null; } catch { return false; }
    }

    public void CreateTableIdJson(string schema, string table, string idColumn = "Id", string jsonColumn = "Json")
    {
        using var cmd = _conn.CreateCommand();
        var safe = System.Text.RegularExpressions.Regex.Replace(table, "[^A-Za-z0-9_]+", "_");
        cmd.CommandText = $@"IF OBJECT_ID(N'[{schema}].[{table}]', N'U') IS NULL
BEGIN
    CREATE TABLE [{schema}].[{table}] (
        [{idColumn}] NVARCHAR(128) NOT NULL CONSTRAINT [PK_{safe}_{idColumn}] PRIMARY KEY,
        [{jsonColumn}] NVARCHAR(MAX) NOT NULL
    );
END";
        cmd.ExecuteNonQuery();
    }

    // Create table with provided columns (Id, Json already included in columns list expected by orchestrator)
    public void CreateTableWithColumns(string schema, string table, List<(string Name, Type ClrType, bool Nullable, bool IsComputed, string? JsonPath, bool IsIndexed)> columns)
    {
        using var cmd = _conn.CreateCommand();
        var safe = System.Text.RegularExpressions.Regex.Replace(table, "[^A-Za-z0-9_]+", "_");
        // Build column definitions. Expect Id and Json to be present as first two columns.
        var defs = new System.Text.StringBuilder();
        foreach (var col in columns)
        {
            if (defs.Length > 0) defs.AppendLine(",");
            if (string.Equals(col.Name, "Id", StringComparison.OrdinalIgnoreCase))
            {
                defs.Append($"[{col.Name}] NVARCHAR(128) NOT NULL CONSTRAINT [PK_{safe}_{col.Name}] PRIMARY KEY");
                continue;
            }
            if (string.Equals(col.Name, "Json", StringComparison.OrdinalIgnoreCase))
            {
                defs.Append($"[{col.Name}] NVARCHAR(MAX) NOT NULL");
                continue;
            }
            if (col.IsComputed && !string.IsNullOrEmpty(col.JsonPath))
            {
                // Use PERSISTED since SQL Server supports persisted computed columns
                defs.Append($"[{col.Name}] AS JSON_VALUE([Json], '{col.JsonPath}') PERSISTED");
            }
            else
            {
                var sqlType = MapType(col.ClrType);
                var nullSql = col.Nullable ? " NULL" : " NOT NULL";
                defs.Append($"[{col.Name}] {sqlType}{nullSql}");
            }
        }

        cmd.CommandText = $@"IF OBJECT_ID(N'[{schema}].[{table}]', N'U') IS NULL
BEGIN
    CREATE TABLE [{schema}].[{table}] (
{defs}
    );
END";
        try { cmd.ExecuteNonQuery(); } catch { }

        // Create indexes for any indexed columns
        for (int i = 0; i < columns.Count; i++)
        {
            var c = columns[i];
            if (c.IsIndexed)
            {
                var ixName = $"IX_{table}_{c.Name}";
                CreateIndex(schema, table, ixName, new[] { c.Name }, unique: false);
            }
        }
    }

    public void AddComputedColumnFromJson(string schema, string table, string column, string jsonPath, bool persisted)
    {
        using var cmd = _conn.CreateCommand();
        var persist = persisted ? " PERSISTED" : string.Empty;
        cmd.CommandText = $"ALTER TABLE [{schema}].[{table}] ADD [{column}] AS JSON_VALUE([Json], '{jsonPath}'){persist}";
        try { cmd.ExecuteNonQuery(); } catch { }
    }

    public void AddPhysicalColumn(string schema, string table, string column, Type clrType, bool nullable)
    {
        using var cmd = _conn.CreateCommand();
        var sqlType = MapType(clrType);
        var nullSql = nullable ? " NULL" : " NOT NULL";
        cmd.CommandText = $"ALTER TABLE [{schema}].[{table}] ADD [{column}] {sqlType}{nullSql}";
        try { cmd.ExecuteNonQuery(); } catch { }
    }

    public void CreateIndex(string schema, string table, string indexName, IReadOnlyList<string> columns, bool unique)
    {
        using var cmd = _conn.CreateCommand();
        var uq = unique ? "UNIQUE " : string.Empty;
        var cols = string.Join(", ", columns.Select(c => $"[{c}]"));
        cmd.CommandText = $@"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'{indexName}' AND object_id = OBJECT_ID(N'[{schema}].[{table}]'))
CREATE {uq}INDEX [{indexName}] ON [{schema}].[{table}] ({cols});";
        try { cmd.ExecuteNonQuery(); } catch { }
    }

    private static string MapType(Type clr)
    {
        clr = Nullable.GetUnderlyingType(clr) ?? clr;
        if (clr == typeof(int)) return "INT";
        if (clr == typeof(long)) return "BIGINT";
        if (clr == typeof(short)) return "SMALLINT";
        if (clr == typeof(bool)) return "BIT";
        if (clr == typeof(DateTime)) return "DATETIME2";
        if (clr == typeof(decimal)) return "DECIMAL(18,2)";
        if (clr == typeof(double)) return "FLOAT";
        if (clr == typeof(Guid)) return "UNIQUEIDENTIFIER";
        return "NVARCHAR(256)";
    }
}
