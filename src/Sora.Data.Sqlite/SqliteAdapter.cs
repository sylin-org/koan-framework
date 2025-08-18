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
using Microsoft.Extensions.Options;
using Sora.Core;
using Sora.Data.Abstractions;
using Sora.Data.Abstractions.Instructions;
using Sora.Data.Abstractions.Naming;
using Sora.Data.Core;
using Sora.Data.Relational.Linq;

namespace Sora.Data.Sqlite;

public sealed class SqliteOptions
{
    [Required]
    public string ConnectionString { get; set; } = "Data Source=./data/app.db";
    public StorageNamingStyle NamingStyle { get; set; } = StorageNamingStyle.FullNamespace;
    public string Separator { get; set; } = ".";
}

public static class SqliteRegistration
{
    public static IServiceCollection AddSqliteAdapter(this IServiceCollection services, Action<SqliteOptions>? configure = null)
    {
        services.AddOptions<SqliteOptions>().ValidateDataAnnotations();
        if (configure is not null) services.Configure(configure);
        services.AddSingleton<IDataAdapterFactory, SqliteAdapterFactory>();
        return services;
    }
}

// legacy initializer removed in favor of standardized auto-registrar

internal sealed class SqliteOptionsConfigurator(IConfiguration config) : IConfigureOptions<SqliteOptions>
{
    public void Configure(SqliteOptions options)
    {
        config.GetSection("Sora:Data:Sqlite").Bind(options);
        config.GetSection("Sora:Data:Sources:Default:sqlite").Bind(options);
        var cs = config.GetConnectionString("Default");
        if (!string.IsNullOrWhiteSpace(cs)) options.ConnectionString = cs!;
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
    IQueryCapabilities,
    IWriteCapabilities,
    IBulkUpsert<TKey>,
    IBulkDelete<TKey>,
    IInstructionExecutor<TEntity>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    public QueryCapabilities Capabilities => QueryCapabilities.Linq | QueryCapabilities.String;
    public WriteCapabilities Writes => WriteCapabilities.BulkUpsert | WriteCapabilities.BulkDelete;

    private readonly IServiceProvider _sp;
    private readonly SqliteOptions _options;
    private readonly IStorageNameResolver _nameResolver;
    private readonly StorageNameResolver.Convention _conv;

    public SqliteRepository(IServiceProvider sp, SqliteOptions options, IStorageNameResolver resolver)
    {
        _sp = sp;
        _options = options;
        _nameResolver = resolver;
        _conv = new StorageNameResolver.Convention(options.NamingStyle, options.Separator, NameCasing.AsIs);
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
        using var cmd = conn.CreateCommand();
        // Real columns for Id, Title, Meta plus Json for full fidelity. Title/Meta are optional.
        cmd.CommandText = $@"CREATE TABLE IF NOT EXISTS [{TableName}] (
    Id TEXT PRIMARY KEY,
    Title TEXT NULL,
    Meta TEXT NULL,
    Json TEXT NOT NULL DEFAULT '{{}}'
);";
        cmd.ExecuteNonQuery();
        // Optional: simple index on Title if present
        try
        {
            using var idx = conn.CreateCommand();
            idx.CommandText = $"CREATE INDEX IF NOT EXISTS [{TableName}_idx_title] ON [{TableName}] (Title);";
            idx.ExecuteNonQuery();
        }
        catch { /* ignore */ }
    }

    private void EnsureTable(IDbConnection db)
    {
        if (db is SqliteConnection sc)
        {
            EnsureTable(sc);
        }
        else
        {
            using var cmd = db.CreateCommand();
            cmd.CommandText = $@"CREATE TABLE IF NOT EXISTS [{TableName}] (
    Id TEXT PRIMARY KEY,
    Title TEXT NULL,
    Meta TEXT NULL,
    Json TEXT NOT NULL
);";
            cmd.ExecuteNonQuery();
        }
    }

    // Basic serialization helpers
    private static TEntity FromRow((string Id, string Json) row)
        => System.Text.Json.JsonSerializer.Deserialize<TEntity>(row.Json)!;
    private static (string Id, string? Title, string? Meta, string Json) ToRowFull(TEntity e)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(e);
        var id = e.Id!.ToString()!;
        // try extract common columns when they exist
        string? title = null; string? metaStr = null;
        var t = typeof(TEntity);
        var titleProp = t.GetProperty("Title", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
        if (titleProp is not null)
        {
            var val = titleProp.GetValue(e);
            if (val is not null) title = val.ToString();
        }
        var metaProp = t.GetProperty("Meta", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
        if (metaProp is not null)
        {
            var val = metaProp.GetValue(e);
            if (val is not null)
            {
                metaStr = val is string s ? s : System.Text.Json.JsonSerializer.Serialize(val);
            }
        }
        return (id, title, metaStr, json);
    }

    public async Task<TEntity?> GetAsync(TKey id, CancellationToken ct = default)
    {
    ct.ThrowIfCancellationRequested();
        using var conn = Open();
        var row = await conn.QuerySingleOrDefaultAsync<(string Id, string Json)>($"SELECT Id, Json FROM [{TableName}] WHERE Id = @Id", new { Id = id!.ToString()! });
        return row == default ? null : FromRow(row);
    }

    public async Task<IReadOnlyList<TEntity>> QueryAsync(object? query, CancellationToken ct = default)
    {
    ct.ThrowIfCancellationRequested();
        using var conn = Open();
        var rows = await conn.QueryAsync<(string Id, string Json)>($"SELECT Id, Json FROM [{TableName}]");
        return rows.Select(FromRow).ToList();
    }

    public async Task<int> CountAsync(object? query, CancellationToken ct = default)
    {
    ct.ThrowIfCancellationRequested();
        using var conn = Open();
        return await conn.ExecuteScalarAsync<int>($"SELECT COUNT(1) FROM [{TableName}]");
    }

    public async Task<IReadOnlyList<TEntity>> QueryAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken ct = default)
    {
        // Minimal: load all then filter in-memory; future: translate to SQL
    ct.ThrowIfCancellationRequested();
    var all = await QueryAsync((object?)null, ct);
        return all.AsQueryable().Where(predicate).ToList();
    }

    public Task<int> CountAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken ct = default)
    => Task.FromResult(QueryAsync(predicate, ct).Result.Count);

    public async Task<IReadOnlyList<TEntity>> QueryAsync(string sql, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
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
                EnsureTable(conn);
                var rows = await conn.QueryAsync(rewritten);
                return MapRowsToEntities(rows);
            }
        }
        else
        {
            var rows = await conn.QueryAsync<(string Id, string Json)>($"SELECT Id, Json FROM [{TableName}] WHERE " + sql);
            return rows.Select(FromRow).ToList();
        }
    }

    public async Task<IReadOnlyList<TEntity>> QueryAsync(string sql, object? parameters, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
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
                EnsureTable(conn);
                var rows = await conn.QueryAsync(rewritten, parameters);
                return MapRowsToEntities(rows);
            }
        }
        else
        {
            var rows = await conn.QueryAsync<(string Id, string Json)>($"SELECT Id, Json FROM [{TableName}] WHERE " + sql, parameters);
            return rows.Select(FromRow).ToList();
        }
    }

    public async Task<int> CountAsync(string sql, CancellationToken ct = default)
    {
    ct.ThrowIfCancellationRequested();
        using var conn = Open();
        return await conn.ExecuteScalarAsync<int>($"SELECT COUNT(1) FROM [{TableName}] WHERE " + sql);
    }

    public Task<int> CountAsync(string sql, object? parameters, CancellationToken ct = default)
        => Task.FromResult(QueryAsync(sql, parameters, ct).Result.Count);

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
        using var tx = conn.BeginTransaction();
        var count = 0;
        foreach (var e in models)
        {
            ct.ThrowIfCancellationRequested();
            var row = ToRowFull(e);
            await conn.ExecuteAsync($"INSERT INTO [{TableName}] (Id, Title, Meta, Json) VALUES (@Id, @Title, @Meta, @Json) ON CONFLICT(Id) DO UPDATE SET Title = excluded.Title, Meta = excluded.Meta, Json = excluded.Json;", new { row.Id, row.Title, row.Meta, row.Json }, tx);
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
            if (upserts.Any()) await repo.UpsertManyAsync(upserts, ct);
            var deleted = 0; if (_deletes.Any()) deleted = await repo.DeleteManyAsync(_deletes, ct);
            return new BatchResult(added, updated, deleted);
        }
    }

    // IInstructionExecutor implementation for schema and raw SQL helpers
    public async Task<TResult> ExecuteAsync<TResult>(Instruction instruction, CancellationToken ct = default)
    {
    using var conn = new SqliteConnection(_options.ConnectionString);
        await conn.OpenAsync(ct);
        switch (instruction.Name)
        {
            case "relational.schema.ensureCreated":
                EnsureTable(conn);
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
                    EnsureTable(conn);
                    var result = await conn.ExecuteScalarAsync(sql, p);
                    return CastScalar<TResult>(result);
                }
            }
            case "relational.sql.nonquery":
            {
                var sql = RewriteEntityToken(GetSqlFromInstruction(instruction));
                // Ensure table exists if targeting this entity table
                try { EnsureTable(conn); } catch { }
                var p = GetParamsFromInstruction(instruction);
                try
                {
                    var affected = await conn.ExecuteAsync(sql, p);
                    object res = affected;
                    return (TResult)res;
                }
                catch (SqliteException ex) when (IsNoSuchTableForEntity(ex))
                {
                    EnsureTable(conn);
                    var affected = await conn.ExecuteAsync(sql, p);
                    object res = affected;
                    return (TResult)res;
                }
            }
            default:
                throw new NotSupportedException($"Instruction '{instruction.Name}' not supported by SQLite adapter for {typeof(TEntity).Name}.");
        }
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
        return msg.IndexOf("no such table", StringComparison.OrdinalIgnoreCase) >= 0
               && msg.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0;
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
