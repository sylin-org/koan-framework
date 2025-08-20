using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Npgsql;
using System.Reflection;

namespace Sora.Data.Core.Direct;

public sealed class DirectDataService(IServiceProvider sp, IConfiguration config) : IDirectDataService
{
    public IDirectSession Direct(string sourceOrAdapter)
        => new DirectSession(sp, config, sourceOrAdapter);
}

internal sealed class DirectSession(IServiceProvider sp, IConfiguration cfg, string sourceOrAdapter) : IDirectSession
{
    private readonly IServiceProvider _sp = sp;
    private readonly IConfiguration _cfg = cfg;
    private string _source = sourceOrAdapter;
    private string? _connectionString;
    private TimeSpan _timeout = TimeSpan.FromSeconds(30);
    private int _maxRows = 10_000;

    public IDirectSession WithConnectionString(string value)
    {
        _connectionString = value; return this;
    }
    public IDirectSession WithTimeout(TimeSpan timeout)
    {
        _timeout = timeout; return this;
    }
    public IDirectSession WithMaxRows(int maxRows)
    {
        _maxRows = maxRows > 0 ? maxRows : _maxRows; return this;
    }

    public IDirectTransaction Begin(CancellationToken ct = default)
    {
        var (provider, connStr) = Resolve();
        var conn = CreateConnection(provider, connStr);
        conn.Open();
        var tx = conn.BeginTransaction();
        return new DirectTransaction(conn, tx, _timeout, _maxRows);
    }

    public async Task<int> Execute(string sql, object? parameters = null, CancellationToken ct = default)
    {
        await using var ctx = await OpenAsync(ct);
    var dyn = ToDictionary(parameters);
        return await ctx.Connection.ExecuteAsync(sql, dyn, ctx.Transaction, commandTimeout: (int)_timeout.TotalSeconds);
    }

    public async Task<T?> Scalar<T>(string sql, object? parameters = null, CancellationToken ct = default)
    {
        await using var ctx = await OpenAsync(ct);
    var dyn = ToDictionary(parameters);
        var res = await ctx.Connection.ExecuteScalarAsync(sql, dyn, ctx.Transaction, commandTimeout: (int)_timeout.TotalSeconds);
        if (res is null || res is DBNull) return default;
        return (T)Convert.ChangeType(res, typeof(T));
    }

    public async Task<IReadOnlyList<object>> Query(string sql, object? parameters = null, CancellationToken ct = default)
    {
        await using var ctx = await OpenAsync(ct);
    var dyn = ToDictionary(parameters);
        using var reader = await ctx.Connection.ExecuteReaderAsync(sql, dyn, ctx.Transaction, commandTimeout: (int)_timeout.TotalSeconds);
        return await MaterializeAsJsonObjects(reader, _maxRows, ct);
    }

    public async Task<IReadOnlyList<T>> Query<T>(string sql, object? parameters = null, CancellationToken ct = default)
    {
        var rows = await Query(sql, parameters, ct);
        var settings = JsonSettings.Default;
        var list = new List<T>(rows.Count);
        foreach (var row in rows)
        {
            var json = row is string s ? s : JsonConvert.SerializeObject(row, settings);
            var item = JsonConvert.DeserializeObject<T>(json, settings);
            if (item != null) list.Add(item);
        }
        return list;
    }

    private async Task<ConnCtx> OpenAsync(CancellationToken ct)
    {
        var (provider, connStr) = Resolve();
        var conn = CreateConnection(provider, connStr);
        await conn.OpenAsync(ct);
        return new ConnCtx(conn);
    }

    private (string provider, string connectionString) Resolve()
    {
        var resolver = _sp.GetService<Sora.Data.Core.Configuration.IDataConnectionResolver>();

        // If a connection string/name was provided explicitly via WithConnectionString, resolve it first.
        if (!string.IsNullOrWhiteSpace(_connectionString))
        {
            var value = _connectionString!;
            // Try configuration via resolver first (providerId = _source when it's a provider key)
            var byResolver = resolver?.Resolve(_source, value);
            if (!string.IsNullOrWhiteSpace(byResolver))
                return (_source, byResolver!);

            // Fallback to ConnectionStrings:name
            var named = _cfg[$"ConnectionStrings:{value}"] ?? _cfg[$"Sora:Data:Sources:{value}:ConnectionString"];
            if (!string.IsNullOrWhiteSpace(named))
            {
                return (_source, named!);
            }
            // Otherwise treat as raw
            return (_source, value);
        }

        // No explicit override: attempt resolver by source name (provider id = _source, name = _source)
        var byName = resolver?.Resolve(_source, _source);
        if (!string.IsNullOrWhiteSpace(byName))
        {
            return (_source, byName!);
        }

        // Fallback to IConfiguration by source name
        var byCfg = _cfg[$"ConnectionStrings:{_source}"] ?? _cfg[$"Sora:Data:Sources:{_source}:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(byCfg))
        {
            return (_source, byCfg!);
        }

        throw new InvalidOperationException($"Connection string for '{_source}' could not be resolved. Use WithConnectionString(nameOrConnectionString) or configure Sora:Data:Sources");
    }

    private static DbConnection CreateConnection(string provider, string connectionString)
        => provider.ToLowerInvariant() switch
        {
            var p when p.Contains("sqlserver") || p.Contains("mssql") => new SqlConnection(connectionString),
            var p when p.Contains("postgres") || p.Contains("npgsql") => new NpgsqlConnection(connectionString),
            var p when p.Contains("sqlite") => new SqliteConnection(connectionString),
            _ => throw new NotSupportedException($"Provider '{provider}' is not supported by DirectSession.")
        };

    internal static async Task<IReadOnlyList<object>> MaterializeAsJsonObjects(DbDataReader reader, int maxRows, CancellationToken ct)
    {
        var list = new List<object>();
        var cols = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
        int count = 0;
        while (await reader.ReadAsync(ct))
        {
            var obj = new Dictionary<string, object?>();
            for (int i = 0; i < cols.Length; i++)
            {
                var val = reader.IsDBNull(i) ? null : reader.GetValue(i);
                obj[cols[i]] = val;
            }
            list.Add(obj);
            if (++count >= maxRows) break;
        }
        return list;
    }

    private static IReadOnlyDictionary<string, object?>? ToDictionary(object? parameters)
    {
        if (parameters is null) return null;
        if (parameters is IReadOnlyDictionary<string, object?> ro) return ro;
        if (parameters is IDictionary<string, object?> dict) return new Dictionary<string, object?>(dict);
        var props = parameters.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var bag = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in props)
        {
            if (!p.CanRead) continue;
            bag[p.Name] = p.GetValue(parameters);
        }
        return bag;
    }

    private sealed record ConnCtx(DbConnection Connection) : IAsyncDisposable
    {
        public DbTransaction? Transaction { get; init; }
        public async ValueTask DisposeAsync()
        {
            await Connection.DisposeAsync();
        }
    }
}

internal sealed class DirectTransaction(DbConnection conn, DbTransaction tx, TimeSpan timeout, int maxRows) : IDirectTransaction
{
    public async Task<int> Execute(string sql, object? parameters = null, CancellationToken ct = default)
        => await conn.ExecuteAsync(sql, DirectSession.ToDictionary(parameters), tx, commandTimeout: (int)timeout.TotalSeconds);

    public async Task<T?> Scalar<T>(string sql, object? parameters = null, CancellationToken ct = default)
    {
    var res = await conn.ExecuteScalarAsync(sql, DirectSession.ToDictionary(parameters), tx, commandTimeout: (int)timeout.TotalSeconds);
        if (res is null || res is DBNull) return default; return (T)Convert.ChangeType(res, typeof(T));
    }

    public async Task<IReadOnlyList<object>> Query(string sql, object? parameters = null, CancellationToken ct = default)
    {
    using var reader = await conn.ExecuteReaderAsync(sql, DirectSession.ToDictionary(parameters), tx, commandTimeout: (int)timeout.TotalSeconds);
        return await DirectSession.MaterializeAsJsonObjects(reader, maxRows, ct);
    }

    public async Task<IReadOnlyList<T>> Query<T>(string sql, object? parameters = null, CancellationToken ct = default)
    {
        var rows = await Query(sql, parameters, ct);
        var settings = JsonSettings.Default;
        var list = new List<T>(rows.Count);
        foreach (var row in rows)
        {
            var json = JsonConvert.SerializeObject(row, settings);
            var item = JsonConvert.DeserializeObject<T>(json, settings);
            if (item != null) list.Add(item);
        }
        return list;
    }

    public Task Commit(CancellationToken ct = default)
    {
        tx.Commit(); return Task.CompletedTask;
    }
    public Task Rollback(CancellationToken ct = default)
    {
        try { tx.Rollback(); } catch { }
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        try { await conn.DisposeAsync(); } catch { }
    }
}

internal static class JsonSettings
{
    public static readonly JsonSerializerSettings Default = new()
    {
        MissingMemberHandling = MissingMemberHandling.Ignore,
        NullValueHandling = NullValueHandling.Include,
        DateParseHandling = DateParseHandling.DateTimeOffset,
        FloatParseHandling = FloatParseHandling.Decimal,
        Culture = System.Globalization.CultureInfo.InvariantCulture,
        Converters = { new StringEnumConverter() }
    };
}
