using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Koan.Data.Abstractions.Instructions;
using Koan.Data.Core.Configuration;
using Koan.Data.Abstractions;
using Koan.Data.Core.Routing;
using Koan.Core.Semantics.Segmentation;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Core.SourceIntegration.Runtime;

namespace Koan.Data.Core.Direct;

internal sealed class DirectSession(IServiceProvider sp, IConfiguration cfg, string? source, string? adapter) : IDirectSession
{
    private readonly IServiceProvider _sp = sp;
    private readonly IConfiguration _cfg = cfg;
    private readonly string? _source = source;
    private readonly string? _adapter = adapter;
    private readonly SegmentationPlan _segmentation = sp.GetRequiredService<SegmentationPlan>();
    private string? _connectionString;
    private TimeSpan _timeout = TimeSpan.FromSeconds(
        (sp.GetService<Microsoft.Extensions.Options.IOptions<Options.DirectOptions>>()?.Value?.TimeoutSeconds) ?? 30);
    private int _maxRows = sp.GetService<Microsoft.Extensions.Options.IOptions<Options.DirectOptions>>()?.Value?.MaxRows ?? 10_000;
    private DataSourcePlan? _ceilingPlan;
    private DataOperationEffect _effect = DataOperationEffect.Unknown;
    private bool _effectDeclared;

    public IDirectSession WithConnectionString(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!IsConcreteConnection(value))
        {
            throw new ArgumentException(
                "WithConnectionString requires a concrete physical connection string; use Direct(source: ...) or Direct(adapter: ...) for provider resolution and 'auto' discovery.",
                nameof(value));
        }

        _connectionString = value;
        _ceilingPlan = null;
        return this;
    }
    public IDirectSession WithTimeout(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        _timeout = timeout; return this;
    }
    public IDirectSession WithMaxRows(int maxRows)
    {
        _maxRows = maxRows > 0 ? maxRows : _maxRows; return this;
    }

    public IDirectSession Effect(DataOperationEffect effect)
    {
        if (effect == DataOperationEffect.Unknown)
            throw new ArgumentOutOfRangeException(nameof(effect), "Declare Read, Write, or SchemaOrAdmin.");
        if (_effectDeclared)
            throw new InvalidOperationException("A Direct session can declare its operation effect once.");
        _effect = effect;
        _effectDeclared = true;
        return this;
    }

    public IDirectTransaction Begin(CancellationToken ct = default)
    {
        var ceiling = DemandBeforeRoute(_effect, "direct transaction begin");
        GuardDirect();
        ct.ThrowIfCancellationRequested();
        var route = Resolve();
        var plan = DemandResolvedRoute(route, ceiling, _effect, "direct transaction begin");
        var binding = Bind(plan);
        var lease = _sp.GetRequiredService<DataOperationHorizon>()
            .Enter(binding, _effect, "direct transaction", ct)
            .AsTask().GetAwaiter().GetResult();
        DbConnection? conn = null;
        try
        {
            conn = CreateConnection(
                _sp,
                route.Provider,
                route.ConnectionString,
                route.Source,
                IsUnqualifiedDefault());
            conn.Open();
            var tx = conn.BeginTransaction();
            return new DirectTransaction(
                conn,
                tx,
                _timeout,
                _maxRows,
                plan,
                _effect,
                _sp.GetRequiredService<RecordSetMaterializer>(),
                lease);
        }
        catch
        {
            conn?.Dispose();
            lease.DisposeAsync().AsTask().GetAwaiter().GetResult();
            throw;
        }
    }

    public async Task<int> Execute(string sql, object? parameters = null, CancellationToken ct = default)
    {
        var ceiling = DemandBeforeRoute(_effect, "direct execute");
        GuardDirect();
        // Prefer instruction executor path when source points to an entity and no explicit connection override is set
        if (_connectionString is null && TryGetEntityType(out var entityType) && TryInvokeExecutor<int>(
                entityType!, InstructionSql.NonQuery(sql, _effect, parameters), ct, out var execTask))
        {
            return await execTask;
        }
        await using var ctx = await Open(_effect, "direct execute", ceiling, ct);
        await using var cmd = CreateCommand(ctx.Connection, sql, ToDictionary(parameters), ctx.Transaction);
        cmd.CommandTimeout = (int)_timeout.TotalSeconds;
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<T?> Scalar<T>(string sql, object? parameters = null, CancellationToken ct = default)
    {
        var ceiling = DemandBeforeRoute(_effect, "direct scalar");
        GuardDirect();
        if (_connectionString is null && TryGetEntityType(out var entityType) && TryInvokeExecutor<T?>(
                entityType!, InstructionSql.Scalar(sql, _effect, parameters), ct, out var execTask))
        {
            return await execTask;
        }
        await using var ctx = await Open(_effect, "direct scalar", ceiling, ct);
        await using var cmd = CreateCommand(ctx.Connection, sql, ToDictionary(parameters), ctx.Transaction);
        cmd.CommandTimeout = (int)_timeout.TotalSeconds;
        var res = await cmd.ExecuteScalarAsync(ct);
        if (res is null || res is DBNull) return default;
        return (T)Convert.ChangeType(res, typeof(T));
    }

    public async Task<IReadOnlyList<object>> Query(string sql, object? parameters = null, CancellationToken ct = default)
    {
        var ceiling = DemandBeforeRoute(_effect, "direct query");
        GuardDirect();
        if (_connectionString is null && TryGetEntityType(out var entityType))
        {
            var data = _sp.GetService(typeof(IDataService)) as IDataService;
            if (data is not null)
            {
                // Execute instruction-backed query and normalize to List<object>
                var instruction = InstructionSql.Query(sql, _effect, parameters);
                var method = typeof(DataServiceExecuteExtensions).GetMethods().FirstOrDefault(m => m.Name == "Execute" && m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 2);
                if (method is not null)
                {
                    var gm = method.MakeGenericMethod(entityType!, typeof(object));
                    var taskObj = gm.Invoke(null, new object?[] { data, instruction, ct }) as Task<object>;
                    if (taskObj is not null)
                    {
                        var result = await taskObj;
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
        await using var ctx = await Open(_effect, "direct query", ceiling, ct);
        await using var cmd = CreateCommand(ctx.Connection, sql, ToDictionary(parameters), ctx.Transaction);
        cmd.CommandTimeout = (int)_timeout.TotalSeconds;
        using var reader = await cmd.ExecuteReaderAsync(ct);
        return await MaterializeAsObjects(reader, _maxRows, ct);
    }

    public async Task<IReadOnlyList<T>> Query<T>(string sql, object? parameters = null, CancellationToken ct = default)
    {
        var ceiling = DemandBeforeRoute(_effect, "direct typed query");
        GuardDirect();
        if (_connectionString is null && TryGetEntityType(out _))
        {
            var entityRows = await Query(sql, parameters, ct).ConfigureAwait(false);
            var typed = new T[entityRows.Count];
            for (var index = 0; index < entityRows.Count; index++)
            {
                if (entityRows[index] is not T item)
                    throw new RecordProjectionException(
                        typeof(T),
                        $"Entity-backed Direct returned '{entityRows[index]?.GetType().FullName ?? "null"}', not the requested target. Use the matching Entity type.");
                typed[index] = item;
            }
            return typed;
        }

        await using var ctx = await Open(_effect, "direct typed query", ceiling, ct);
        await using var cmd = CreateCommand(ctx.Connection, sql, ToDictionary(parameters), ctx.Transaction);
        cmd.CommandTimeout = (int)_timeout.TotalSeconds;
        var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var limits = new RecordSetLimits(
            _maxRows,
            long.MaxValue,
            long.MaxValue,
            _timeout);
        var records = await _sp.GetRequiredService<RecordSetMaterializer>()
            .Materialize(new AdoNeutralRecordReader(reader), limits, "direct typed query", ct)
            .ConfigureAwait(false);
        return records.Project<T>();
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
                try { return a.GetTypes(); } catch { return []; }
            })
            .FirstOrDefault(t => string.Equals(t.FullName, token, StringComparison.OrdinalIgnoreCase) || string.Equals(t.Name, token, StringComparison.Ordinal));
        return entityType is not null;
    }

    private void GuardDirect()
    {
        if (_segmentation.IsEmpty) return;
        var binding = _segmentation.Untyped.Bind("direct data operation");
        if (binding.IsEmpty) return; // an explicit host scope is a deliberate control-plane operation

        throw new NotSupportedException(
            "Direct data operations cannot preserve the application's compiled segmentation guarantee because " +
            "opaque provider commands carry no framework predicate. Use the Entity surface, or establish an " +
            "explicit host scope for a genuine control-plane operation.");
    }

    private bool TryInvokeExecutor<TResult>(
        Type entityType,
        Instruction instruction,
        CancellationToken ct,
        out Task<TResult> task)
    {
        task = default!;
        var data = _sp.GetService(typeof(IDataService)) as IDataService;
        if (data is null) return false;
        var method = typeof(DataServiceExecuteExtensions).GetMethods().FirstOrDefault(m => m.Name == "Execute" && m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 2);
        if (method is null) return false;
        var gm = method.MakeGenericMethod(entityType, typeof(TResult));
        var obj = gm.Invoke(null, new object?[] { data, instruction, ct });
        if (obj is Task<TResult> t)
        {
            task = t; return true;
        }
        return false;
    }

    private async Task<ConnCtx> Open(
        DataOperationEffect effect,
        string operation,
        DataSourcePlan? ceiling,
        CancellationToken ct)
    {
        var route = Resolve();
        var plan = DemandResolvedRoute(route, ceiling, effect, operation);
        var binding = Bind(plan);
        var lease = await _sp.GetRequiredService<DataOperationHorizon>()
            .Enter(binding, effect, operation, ct).ConfigureAwait(false);
        DbConnection? conn = null;
        try
        {
            conn = CreateConnection(
                _sp,
                route.Provider,
                route.ConnectionString,
                route.Source,
                IsUnqualifiedDefault());
            await conn.OpenAsync(ct).ConfigureAwait(false);
            return new ConnCtx(conn, lease);
        }
        catch
        {
            if (conn is not null) await conn.DisposeAsync().ConfigureAwait(false);
            await lease.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private DataSourcePlan? DemandBeforeRoute(DataOperationEffect effect, string operation)
    {
        // The entity pseudo-source routes through RepositoryFacade, whose compiled entity source is authoritative.
        if (_connectionString is null && TryGetEntityType(out _)) return null;

        var registry = _sp.GetService<DataSourceRegistry>();
        if (_connectionString is null && string.IsNullOrWhiteSpace(_source) && string.IsNullOrWhiteSpace(_adapter))
        {
            var active = _sp.GetRequiredService<DefaultDataRouteAuthority>().Current.Plan;
            active.Demand(effect, operation);
            return active;
        }
        if (_ceilingPlan is not null)
        {
            _ceilingPlan.Demand(effect, operation);
            return _ceilingPlan;
        }
        if (registry is null)
        {
            DataSourcePlan.Default.Demand(effect, operation);
            return DataSourcePlan.Default;
        }

        var sourceName = string.IsNullOrWhiteSpace(_source) ? "Default" : _source!;
        var configured = registry.GetSource(sourceName);
        var adapterHint = !string.IsNullOrWhiteSpace(_adapter)
            ? _adapter!
            : !string.IsNullOrWhiteSpace(configured?.Adapter)
                ? configured.Adapter
                : sourceName;
        var plan = registry.GetPlan(sourceName, adapterHint, _connectionString);
        plan.Demand(effect, operation);
        _ceilingPlan = plan;
        return _ceilingPlan;
    }

    private DataSourcePlan DemandResolvedRoute(
        ConnectionRoute route,
        DataSourcePlan? ceiling,
        DataOperationEffect effect,
        string operation)
    {
        var registry = _sp.GetService<DataSourceRegistry>();
        var resolved = registry?.GetPlan(route.Source, route.Provider, route.ConnectionString)
            ?? ceiling
            ?? DataSourcePlan.Default;
        resolved.Demand(effect, operation);
        _sp.GetService<DataDiagnostics>()?.ObserveSourcePlan(resolved);
        return resolved;
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
                // Bind by the logical name: providers match a bare name to their own dialect prefix
                // (`@x` SQLite/SqlServer, `$x` DuckDB), so no single convention is forced on all adapters.
                p.ParameterName = kv.Key.TrimStart('@', '$');
                p.Value = kv.Value ?? DBNull.Value;
                cmd.Parameters.Add(p);
            }
        }
        return cmd;
    }

    private ConnectionRoute Resolve()
    {
        var resolver = _sp.GetService(typeof(IDataConnectionResolver)) as IDataConnectionResolver;
        var sourceRegistry = _sp.GetService(typeof(Core.DataSourceRegistry)) as Core.DataSourceRegistry;

        // Priority 1: Explicit connection string override via WithConnectionString
        if (_connectionString is not null)
        {
            var value = _connectionString!;
            var sourceKey = string.IsNullOrWhiteSpace(_source) ? "Default" : _source!;
            var providerHint = _adapter;
            if (string.IsNullOrWhiteSpace(providerHint)
                && !string.IsNullOrWhiteSpace(_source)
                && sourceRegistry?.TryGetSource(_source, out var configuredSource) == true)
            {
                providerHint = configuredSource.Adapter;
            }
            providerHint ??= _source ?? "Default";
            return new ConnectionRoute(providerHint, value, sourceKey);
        }

        // Priority 2: Source routing. The provider connection factory owns physical resolution so Direct shares
        // provider-scoped configuration, discovery, and unresolved-intent handling with Entity repositories.
        if (!string.IsNullOrWhiteSpace(_source))
        {
            if (sourceRegistry?.TryGetSource(_source, out var sourceDef) == true)
            {
                if (string.IsNullOrWhiteSpace(sourceDef.Adapter))
                {
                    throw new InvalidOperationException(
                        $"Source '{_source}' does not specify an adapter. Add 'Adapter' to Koan:Data:Sources:{_source}.");
                }

                return ResolveProviderSource(
                    sourceDef.Adapter,
                    _source,
                    sourceDef.ConnectionString,
                    resolver);
            }

            // Fallback: Try config-based resolution for backward compatibility
            var byCfg = _cfg[$"ConnectionStrings:{_source}"] ?? _cfg[ConfigurationConstants.Keys.SourceConnectionString(_source)];
            if (!string.IsNullOrWhiteSpace(byCfg))
                return ResolveProviderSource(_source, _source, byCfg, resolver);

            throw new InvalidOperationException(
                $"Source '{_source}' not found in DataSourceRegistry. Configure Koan:Data:Sources:{_source} or use WithConnectionString().");
        }

        // Priority 3: Adapter routing (use adapter as provider, resolve default connection)
        if (!string.IsNullOrWhiteSpace(_adapter))
        {
            string? sourceFallback = null;
            if (sourceRegistry?.TryGetSource("Default", out var defaultSource) == true)
            {
                if (string.Equals(defaultSource.Adapter, _adapter, StringComparison.OrdinalIgnoreCase))
                    sourceFallback = defaultSource.ConnectionString;
            }

            sourceFallback ??= _cfg[ConfigurationConstants.Keys.AdapterConnectionString(_adapter)];
            return ResolveProviderSource(_adapter, "Default", sourceFallback, resolver);
        }

        // Priority 4: No routing specified - use default source
        var active = _sp.GetRequiredService<DefaultDataRouteAuthority>().Current.Plan;
        if (sourceRegistry?.TryGetSource(active.Source, out var defSource) == true)
        {
            return ResolveProviderSource(
                active.Adapter,
                active.Source,
                defSource.ConnectionString,
                resolver);
        }

        throw new InvalidOperationException(
            $"The active default source '{active.Source}' is not configured. Restore the source declaration that matches the durable route record.");
    }

    private ConnectionRoute ResolveProviderSource(
        string provider,
        string source,
        string? concreteFallback,
        IDataConnectionResolver? resolver)
    {
        var factory = _sp.GetServices<IDataProviderConnectionFactory>()
            .FirstOrDefault(candidate => candidate.CanHandle(provider))
            ?? throw new NotSupportedException(
                $"No IDataProviderConnectionFactory registered for provider '{provider}'. " +
                "Make sure the corresponding adapter package is referenced and registered.");

        var resolved = factory.ResolveConnectionString(source);
        if (!IsConcreteConnection(resolved))
        {
            resolved = resolver?.Resolve(provider, source);
        }
        if (!IsConcreteConnection(resolved))
        {
            resolved = concreteFallback;
        }
        if (!IsConcreteConnection(resolved))
        {
            throw new InvalidOperationException(
                $"Connection string for provider '{provider}', source '{source}' remains '{resolved ?? "unconfigured"}'. " +
                "Configure a concrete provider/source connection or reference a provider that resolves autonomous discovery for Direct operations.");
        }

        return new ConnectionRoute(provider, resolved!, source);
    }

    private static bool IsConcreteConnection(string? value)
        => !string.IsNullOrWhiteSpace(value)
           && !string.Equals(value.Trim(), "auto", StringComparison.OrdinalIgnoreCase);

    private static DbConnection CreateConnection(
        IServiceProvider sp,
        string provider,
        string connectionString,
        string source,
        bool defaultDerived)
    {
        var factories = sp.GetServices<IDataProviderConnectionFactory>()
                        ?? Enumerable.Empty<IDataProviderConnectionFactory>();
        var factory = factories.FirstOrDefault(f => f.CanHandle(provider));
        if (factory is null)
        {
            throw new NotSupportedException($"No IDataProviderConnectionFactory registered for provider '{provider}'. Make sure the corresponding adapter package is referenced and registered.");
        }
        var canonicalProvider = sp.GetService<DataProviderCatalog>()
            ?.Find(provider)
            ?.Provider ?? provider;
        sp.GetService<DataDiagnostics>()?.ObserveParticipation(
            canonicalProvider,
            source,
            defaultDerived
                ? DataAdapterParticipationRole.DefaultDerived
                : DataAdapterParticipationRole.Explicit);
        return factory.Create(connectionString, source);
    }

    private sealed record ConnectionRoute(string Provider, string ConnectionString, string Source);

    internal sealed class AdoNeutralRecordReader : INeutralRecordReader
    {
        private readonly DbDataReader _reader;
        private bool _ended;
        private bool _additional;

        public AdoNeutralRecordReader(DbDataReader reader)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            var fields = new DataField[reader.FieldCount];
            for (var ordinal = 0; ordinal < fields.Length; ordinal++)
            {
                fields[ordinal] = new DataField(
                    ordinal,
                    reader.GetName(ordinal),
                    SafeFieldType(reader, ordinal),
                    SafeProviderType(reader, ordinal));
            }
            Fields = fields;
        }

        public IReadOnlyList<DataField> Fields { get; }
        public NeutralRecordReaderCompletion Completion => NeutralRecordReaderCompletion.Complete;
        public bool HasAdditionalResultChannels => _additional;

        public async ValueTask<DataRecord?> Read(CancellationToken ct = default)
        {
            if (_ended) return null;
            if (!await _reader.ReadAsync(ct).ConfigureAwait(false))
            {
                _ended = true;
                _additional = await _reader.NextResultAsync(ct).ConfigureAwait(false);
                return null;
            }

            var values = new object?[Fields.Count];
            for (var ordinal = 0; ordinal < values.Length; ordinal++)
                values[ordinal] = _reader.IsDBNull(ordinal) ? null : Normalize(_reader.GetValue(ordinal));
            return new DataRecord(Fields, values);
        }

        public ValueTask DisposeAsync() => _reader.DisposeAsync();

        private static object? Normalize(object? value) => value switch
        {
            DBNull => null,
            char character => character.ToString(),
            _ => value
        };

        private static Type? SafeFieldType(DbDataReader reader, int ordinal)
        {
            try { return reader.GetFieldType(ordinal); }
            catch (NotSupportedException) { return null; }
        }

        private static string? SafeProviderType(DbDataReader reader, int ordinal)
        {
            try { return reader.GetDataTypeName(ordinal); }
            catch (NotSupportedException) { return null; }
        }
    }

    internal static async Task<IReadOnlyList<object>> MaterializeAsObjects(DbDataReader reader, int maxRows, CancellationToken ct)
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

    private DataRouteBinding Bind(DataSourcePlan plan)
    {
        var origin = IsUnqualifiedDefault()
            ? DataRouteOrigin.Default
            : DataRouteOrigin.ExplicitSource;
        return _sp.GetRequiredService<DefaultDataRouteAuthority>().Bind(plan, origin);
    }

    private bool IsUnqualifiedDefault() =>
        _connectionString is null && string.IsNullOrWhiteSpace(_source) && string.IsNullOrWhiteSpace(_adapter);

    private sealed record ConnCtx(DbConnection Connection, DataOperationLease Lease) : IAsyncDisposable
    {
        public DbTransaction? Transaction { get; init; }
        public async ValueTask DisposeAsync()
        {
            try
            {
                await Connection.DisposeAsync();
            }
            finally
            {
                await Lease.DisposeAsync();
            }
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
