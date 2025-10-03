using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Koan.Data.Abstractions.Instructions;
using Koan.Data.Core;
using Koan.Data.Core.Configuration;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Koan.Data.Direct;

internal sealed class DirectSession(IServiceProvider sp, IConfiguration cfg, string? source, string? adapter) : Koan.Data.Core.Direct.IDirectSession
{
    private readonly IServiceProvider _sp = sp;
    private readonly IConfiguration _cfg = cfg;
    private readonly string? _source = source;
    private readonly string? _adapter = adapter;
    private string? _connectionString;
    private TimeSpan _timeout = TimeSpan.FromSeconds(
        (sp.GetService<Microsoft.Extensions.Options.IOptions<Core.Options.DirectOptions>>()?.Value?.TimeoutSeconds) ?? 30);
    private int _maxRows = sp.GetService<Microsoft.Extensions.Options.IOptions<Core.Options.DirectOptions>>()?.Value?.MaxRows ?? 10_000;

    public Koan.Data.Core.Direct.IDirectSession WithConnectionString(string value)
    {
        _connectionString = value; return this;
    }
    public Koan.Data.Core.Direct.IDirectSession WithTimeout(TimeSpan timeout)
    {
        _timeout = timeout; return this;
    }
    public Koan.Data.Core.Direct.IDirectSession WithMaxRows(int maxRows)
    {
        _maxRows = maxRows > 0 ? maxRows : _maxRows; return this;
    }

    public Koan.Data.Core.Direct.IDirectTransaction Begin(CancellationToken ct = default)
    {
        var (provider, connStr) = Resolve();
        var conn = CreateConnection(_sp, provider, connStr);
        conn.Open();
        var tx = conn.BeginTransaction();
        return new DirectTransaction(conn, tx, _timeout, _maxRows);
    }

    public async Task<int> Execute(string sql, object? parameters = null, CancellationToken ct = default)
    {
        // Prefer instruction executor path when source points to an entity and no explicit connection override is set
        if (_connectionString is null && TryGetEntityType(out var entityType) && TryInvokeExecutor<int>(entityType!, InstructionSql.NonQuery(sql, parameters), out var execTask))
        {
            return await execTask.ConfigureAwait(false);
        }
        await using var ctx = await OpenAsync(ct);
        await using var cmd = CreateCommand(ctx.Connection, sql, ToDictionary(parameters), ctx.Transaction);
        cmd.CommandTimeout = (int)_timeout.TotalSeconds;
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<T?> Scalar<T>(string sql, object? parameters = null, CancellationToken ct = default)
    {
        if (_connectionString is null && TryGetEntityType(out var entityType) && TryInvokeExecutor<T?>(entityType!, InstructionSql.Scalar(sql, parameters), out var execTask))
        {
            return await execTask.ConfigureAwait(false);
        }
        await using var ctx = await OpenAsync(ct);
        await using var cmd = CreateCommand(ctx.Connection, sql, ToDictionary(parameters), ctx.Transaction);
        cmd.CommandTimeout = (int)_timeout.TotalSeconds;
        var res = await cmd.ExecuteScalarAsync(ct);
        if (res is null || res is DBNull) return default;
        return (T)Convert.ChangeType(res, typeof(T));
    }

    public async Task<IReadOnlyList<object>> Query(string sql, object? parameters = null, CancellationToken ct = default)
    {
        if (_connectionString is null && TryGetEntityType(out var entityType))
        {
            var data = _sp.GetService(typeof(IDataService)) as IDataService;
            if (data is not null)
            {
                // Execute instruction-backed query and normalize to List<object>
                var instruction = InstructionSql.Query(sql, parameters);
                var method = typeof(DataServiceExecuteExtensions).GetMethods().FirstOrDefault(m => m.Name == "Execute" && m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 2);
                if (method is not null)
                {
                    var gm = method.MakeGenericMethod(entityType!, typeof(object));
                    var taskObj = gm.Invoke(null, new object?[] { data, instruction, ct }) as Task<object>;
                    if (taskObj is not null)
                    {
                        var result = await taskObj.ConfigureAwait(false);
                        if (result is System.Collections.IEnumerable seq && result is not string)
                        {
                            var list = new List<object>();
                            foreach (var item in seq) list.Add(item!);
                            return list;
                        }
                        return new List<object> { result! };
                    }
                }
            }
        }
        await using var ctx = await OpenAsync(ct);
        await using var cmd = CreateCommand(ctx.Connection, sql, ToDictionary(parameters), ctx.Transaction);
        cmd.CommandTimeout = (int)_timeout.TotalSeconds;
        using var reader = await cmd.ExecuteReaderAsync(ct);
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

    private bool TryGetEntityType(out Type? entityType)
    {
        entityType = null;
        if (string.IsNullOrWhiteSpace(_source)) return false;
        const string prefix = "entity:";
        if (!_source.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        var token = _source.Substring(prefix.Length).Trim();
        if (string.IsNullOrWhiteSpace(token)) return false;
        // Try fully-qualified first
        entityType = Type.GetType(token, throwOnError: false, ignoreCase: true);
        if (entityType is not null) return true;
        // Fallback: search loaded assemblies for simple name match, preferring Koan.* assemblies
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        entityType = assemblies
            .OrderBy(a => a.GetName().Name?.StartsWith("Koan.") == true ? 0 : 1)
            .SelectMany(a =>
            {
                try { return a.GetTypes(); } catch { return Array.Empty<Type>(); }
            })
            .FirstOrDefault(t => string.Equals(t.FullName, token, StringComparison.OrdinalIgnoreCase) || string.Equals(t.Name, token, StringComparison.Ordinal));
        return entityType is not null;
    }

    private bool TryInvokeExecutor<TResult>(Type entityType, Instruction instruction, out Task<TResult> task)
    {
        task = default!;
        var data = _sp.GetService(typeof(IDataService)) as IDataService;
        if (data is null) return false;
        var method = typeof(DataServiceExecuteExtensions).GetMethods().FirstOrDefault(m => m.Name == "Execute" && m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 2);
        if (method is null) return false;
        var gm = method.MakeGenericMethod(entityType, typeof(TResult));
        var obj = gm.Invoke(null, new object?[] { data, instruction, default(CancellationToken) });
        if (obj is Task<TResult> t)
        {
            task = t; return true;
        }
        return false;
    }

    private async Task<ConnCtx> OpenAsync(CancellationToken ct)
    {
        var (provider, connStr) = Resolve();
        var conn = CreateConnection(_sp, provider, connStr);
        await conn.OpenAsync(ct);
        return new ConnCtx(conn);
    }

    private static DbCommand CreateCommand(DbConnection connection, string sql, IReadOnlyDictionary<string, object?>? parameters, DbTransaction? tx)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        if (tx is not null) cmd.Transaction = tx;
        if (parameters is not null)
        {
            foreach (var kv in parameters)
            {
                var p = cmd.CreateParameter();
                p.ParameterName = kv.Key.StartsWith("@") ? kv.Key : "@" + kv.Key;
                p.Value = kv.Value ?? DBNull.Value;
                cmd.Parameters.Add(p);
            }
        }
        return cmd;
    }

    private (string provider, string connectionString) Resolve()
    {
        var resolver = _sp.GetService(typeof(IDataConnectionResolver)) as IDataConnectionResolver;
        var sourceRegistry = _sp.GetService(typeof(Core.DataSourceRegistry)) as Core.DataSourceRegistry;

        // Priority 1: Explicit connection string override via WithConnectionString
        if (!string.IsNullOrWhiteSpace(_connectionString))
        {
            var value = _connectionString!;
            var providerHint = _adapter ?? _source ?? "Default";

            var byResolver = resolver?.Resolve(providerHint, value);
            if (!string.IsNullOrWhiteSpace(byResolver))
                return (providerHint, byResolver!);

            var named = _cfg[$"ConnectionStrings:{value}"] ?? _cfg[$"Koan:Data:Sources:{value}:ConnectionString"];
            if (!string.IsNullOrWhiteSpace(named))
                return (providerHint, named!);

            return (providerHint, value);
        }

        // Priority 2: Source routing (look up source definition to get adapter + connection)
        if (!string.IsNullOrWhiteSpace(_source))
        {
            if (sourceRegistry?.TryGetSource(_source, out var sourceDef) == true)
            {
                return (sourceDef.Adapter, sourceDef.ConnectionString);
            }

            // Fallback: Try config-based resolution for backward compatibility
            var byCfg = _cfg[$"ConnectionStrings:{_source}"] ?? _cfg[$"Koan:Data:Sources:{_source}:ConnectionString"];
            if (!string.IsNullOrWhiteSpace(byCfg))
                return (_source, byCfg!);

            throw new InvalidOperationException(
                $"Source '{_source}' not found in DataSourceRegistry. Configure Koan:Data:Sources:{_source} or use WithConnectionString().");
        }

        // Priority 3: Adapter routing (use adapter as provider, resolve default connection)
        if (!string.IsNullOrWhiteSpace(_adapter))
        {
            var byResolver = resolver?.Resolve(_adapter, _adapter);
            if (!string.IsNullOrWhiteSpace(byResolver))
                return (_adapter, byResolver!);

            // Try adapter-specific config path
            var adapterCfg = _cfg[$"Koan:Data:{_adapter}:ConnectionString"];
            if (!string.IsNullOrWhiteSpace(adapterCfg))
                return (_adapter, adapterCfg!);

            // Try default source for this adapter
            if (sourceRegistry?.TryGetSource("Default", out var defaultSource) == true)
            {
                if (string.Equals(defaultSource.Adapter, _adapter, StringComparison.OrdinalIgnoreCase))
                    return (_adapter, defaultSource.ConnectionString);
            }

            throw new InvalidOperationException(
                $"Connection string for adapter '{_adapter}' could not be resolved. Use WithConnectionString() or configure Koan:Data:{_adapter}:ConnectionString.");
        }

        // Priority 4: No routing specified - use default source
        if (sourceRegistry?.TryGetSource("Default", out var defSource) == true)
        {
            return (defSource.Adapter, defSource.ConnectionString);
        }

        throw new InvalidOperationException(
            "No source or adapter specified, and no 'Default' source configured. Specify Direct(source: ...) or Direct(adapter: ...) or configure Koan:Data:Sources:Default.");
    }

    private static DbConnection CreateConnection(IServiceProvider sp, string provider, string connectionString)
    {
        var factories = sp.GetServices<IDataProviderConnectionFactory>()
                        ?? Enumerable.Empty<IDataProviderConnectionFactory>();
        var factory = factories.FirstOrDefault(f => f.CanHandle(provider));
        if (factory is null)
        {
            throw new NotSupportedException($"No IDataProviderConnectionFactory registered for provider '{provider}'. Make sure the corresponding adapter package is referenced and registered.");
        }
        return factory.Create(connectionString);
    }

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

    private sealed record ConnCtx(DbConnection Connection) : IAsyncDisposable
    {
        public DbTransaction? Transaction { get; init; }
        public async ValueTask DisposeAsync()
        {
            await Connection.DisposeAsync();
        }
    }

    internal static IReadOnlyDictionary<string, object?>? ToDictionary(object? parameters)
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
}