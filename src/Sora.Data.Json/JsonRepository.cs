using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sora.Data.Abstractions;
using Sora.Data.Json.Infrastructure;
using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using System.Text.Json;

namespace Sora.Data.Json;

/// <summary>
/// Options for the JSON file-backed data adapter.
/// </summary>
public sealed class JsonDataOptions
{
    [Required]
    public string DirectoryPath { get; set; } = ".\\data";
    // Paging guardrails per acceptance criteria
    // Applied when caller does not provide explicit paging; caps server-side where possible or in fallback paths
    public int DefaultPageSize { get; set; } = 1000;
    public int MaxPageSize { get; set; } = 10_000;
}

public static class JsonDataServiceCollectionExtensions
{
    /// <summary>
    /// Register a JSON repository for a specific aggregate pair.
    /// </summary>
    public static IServiceCollection AddJsonData<TEntity, TKey>(this IServiceCollection services, Action<JsonDataOptions>? configure = null)
    where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        services.AddOptions<JsonDataOptions>();
        if (configure is not null) services.Configure(configure);
        services.AddSingleton<IDataRepository<TEntity, TKey>, JsonRepository<TEntity, TKey>>();
        return services;
    }
}

[Sora.Data.Abstractions.ProviderPriority(0)]
public sealed class JsonAdapterFactory : IDataAdapterFactory
{
    public bool CanHandle(string provider) => string.Equals(provider, "json", StringComparison.OrdinalIgnoreCase);

    public IDataRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider sp)
    where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var opts = sp.GetRequiredService<IOptions<JsonDataOptions>>();
        return new JsonRepository<TEntity, TKey>(opts);
    }
}

public static class JsonAdapterRegistration
{
    /// <summary>
    /// Register the JSON adapter for discovery; optionally configure options.
    /// </summary>
    public static IServiceCollection AddJsonAdapter(this IServiceCollection services, Action<JsonDataOptions>? configure = null)
    {
        services.AddOptions<JsonDataOptions>();
        if (configure is not null) services.Configure(configure);
        services.AddSingleton<IDataAdapterFactory, JsonAdapterFactory>();
        return services;
    }
}

// Self-registration hook so the adapter is available with AddSoraDataCore()
/// <summary>
/// Auto-registers the JSON adapter and health contributor during Sora initialization.
/// </summary>
// legacy initializer removed in favor of standardized auto-registrar

internal sealed class JsonDataOptionsConfigurator : Microsoft.Extensions.Options.IConfigureOptions<JsonDataOptions>
{
    private readonly IConfiguration? _config;
    // Prefer IConfiguration when available; do not fail if it's missing (non-host apps)
    public JsonDataOptionsConfigurator() { }
    public JsonDataOptionsConfigurator(IConfiguration config) { _config = config; }
    public void Configure(JsonDataOptions options)
    {
        // ADR-0040: avoid Bind; read via helper with centralized keys
        // Prefer explicit DirectoryPath if provided in either section, otherwise keep default
        var dir = Sora.Core.Configuration.ReadFirst(_config, new[]
        {
            $"{Constants.Configuration.Section_Data}:{Constants.Configuration.Keys.DirectoryPath}",
            $"{Constants.Configuration.Section_Sources_Default}:{Constants.Configuration.Keys.DirectoryPath}"
        });
        if (!string.IsNullOrWhiteSpace(dir))
            options.DirectoryPath = dir!;

        // Optional paging guardrails
        var dps = Sora.Core.Configuration.ReadFirst(_config, new[]
        {
            $"{Constants.Configuration.Section_Data}:{Constants.Configuration.Keys.DefaultPageSize}",
            $"{Constants.Configuration.Section_Sources_Default}:{Constants.Configuration.Keys.DefaultPageSize}"
        });
        if (int.TryParse(dps, out var dpsVal) && dpsVal > 0) options.DefaultPageSize = dpsVal;
        var mps = Sora.Core.Configuration.ReadFirst(_config, new[]
        {
            $"{Constants.Configuration.Section_Data}:{Constants.Configuration.Keys.MaxPageSize}",
            $"{Constants.Configuration.Section_Sources_Default}:{Constants.Configuration.Keys.MaxPageSize}"
        });
        if (int.TryParse(mps, out var mpsVal) && mpsVal > 0) options.MaxPageSize = mpsVal;
        if (options.DefaultPageSize > options.MaxPageSize)
            options.DefaultPageSize = options.MaxPageSize;
    }
}

/// <summary>
/// In-memory dictionary with JSON file persistence per aggregate.
/// Supports LINQ queries and bulk upsert/delete.
/// </summary>
internal sealed class JsonRepository<TEntity, TKey> :
    IDataRepository<TEntity, TKey>,
    IDataRepositoryWithOptions<TEntity, TKey>,
    ILinqQueryRepository<TEntity, TKey>,
    ILinqQueryRepositoryWithOptions<TEntity, TKey>,
    IQueryCapabilities,
    IWriteCapabilities,
    Sora.Data.Abstractions.Instructions.IInstructionExecutor<TEntity>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    private readonly string _baseDir;
    // Maintain per-physical-name stores and file paths so different sets are isolated
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<TKey, TEntity>> _stores = new();
    private readonly ConcurrentDictionary<string, string> _files = new();
    public QueryCapabilities Capabilities => QueryCapabilities.Linq; // supports LINQ predicate
    // JSON adapter does not have native bulk APIs; honor semantics via fallbacks without advertising native bulk
    public WriteCapabilities Writes => default;

    public JsonRepository(IOptions<JsonDataOptions> options)
    {
        _options = options;
        _baseDir = options.Value.DirectoryPath;
        Directory.CreateDirectory(_baseDir);
    }

    private readonly IOptions<JsonDataOptions> _options;

    public Task<TEntity?> GetAsync(TKey id, CancellationToken ct = default)
    {
        var store = ResolveStore();
        return Task.FromResult(store.TryGetValue(id, out var value) ? value : null);
    }

    public Task<IReadOnlyList<TEntity>> QueryAsync(object? query, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var store = ResolveStore();
        var pageSize = Math.Max(1, Math.Min(_options.Value.DefaultPageSize, _options.Value.MaxPageSize));
        // Apply default paging guardrail when caller does not specify explicit options interface
        var result = store.Values.Take(pageSize).ToList();
        return Task.FromResult((IReadOnlyList<TEntity>)result);
    }

    public Task<IReadOnlyList<TEntity>> QueryAsync(object? query, DataQueryOptions? options, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var store = ResolveStore();
        var items = store.Values.AsQueryable();
        var max = Math.Max(1, _options.Value.MaxPageSize);
        var size = options?.PageSize is int ps && ps > 0 ? Math.Min(ps, max) : Math.Min(_options.Value.DefaultPageSize, max);
        var page = options?.Page is int p && p > 1 ? p : 1;
        var skip = (page - 1) * size;
        var list = items.Skip(skip).Take(size).ToList();
        return Task.FromResult((IReadOnlyList<TEntity>)list);
    }

    public Task<int> CountAsync(object? query, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var store = ResolveStore();
        return Task.FromResult(store.Count);
    }

    public Task<IReadOnlyList<TEntity>> QueryAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var store = ResolveStore();
        var pageSize = Math.Max(1, Math.Min(_options.Value.DefaultPageSize, _options.Value.MaxPageSize));
        var list = store.Values.AsQueryable().Where(predicate).Take(pageSize).ToList();
        return Task.FromResult((IReadOnlyList<TEntity>)list);
    }

    public Task<IReadOnlyList<TEntity>> QueryAsync(Expression<Func<TEntity, bool>> predicate, DataQueryOptions? options, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var store = ResolveStore();
        var items = store.Values.AsQueryable().Where(predicate);
        var max = Math.Max(1, _options.Value.MaxPageSize);
        var size = options?.PageSize is int ps && ps > 0 ? Math.Min(ps, max) : Math.Min(_options.Value.DefaultPageSize, max);
        var page = options?.Page is int p && p > 1 ? p : 1;
        var skip = (page - 1) * size;
        var list = items.Skip(skip).Take(size).ToList();
        return Task.FromResult((IReadOnlyList<TEntity>)list);
    }

    public Task<int> CountAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var store = ResolveStore();
        return Task.FromResult(store.Values.AsQueryable().Count(predicate));
    }

    public Task<TEntity> UpsertAsync(TEntity model, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var (name, store) = ResolveNameAndStore();
        store[model.Id] = model;
        Persist(name, store);
        return Task.FromResult(model);
    }

    public Task<bool> DeleteAsync(TKey id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var (name, store) = ResolveNameAndStore();
        var ok = store.TryRemove(id, out _);
        if (ok) Persist(name, store);
        return Task.FromResult(ok);
    }

    public Task<int> UpsertManyAsync(IEnumerable<TEntity> models, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var (name, store) = ResolveNameAndStore();
        var count = 0;
        foreach (var m in models)
        {
            store[m.Id] = m; count++;
            ct.ThrowIfCancellationRequested();
        }
        if (count > 0) Persist(name, store);
        return Task.FromResult(count);
    }

    public Task<int> DeleteManyAsync(IEnumerable<TKey> ids, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var (name, store) = ResolveNameAndStore();
        var count = 0;
        foreach (var id in ids)
        {
            if (store.TryRemove(id, out _)) count++;
            ct.ThrowIfCancellationRequested();
        }
        if (count > 0) Persist(name, store);
        return Task.FromResult(count);
    }

    public Task<int> DeleteAllAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var (name, store) = ResolveNameAndStore();
        var deleted = store.Count;
        store.Clear();
        Persist(name, store);
        return Task.FromResult(deleted);
    }

    public IBatchSet<TEntity, TKey> CreateBatch() => new JsonBatch(this);

    // Instruction execution for fast-path operations (e.g., clear all)
    public Task<TResult> ExecuteAsync<TResult>(Sora.Data.Abstractions.Instructions.Instruction instruction, CancellationToken ct = default)
    {
        switch (instruction.Name)
        {
            case global::Sora.Data.DataInstructions.EnsureCreated:
                {
                    Directory.CreateDirectory(_baseDir);
                    // Touch the set file to ensure presence
                    var name = ComputePhysicalName();
                    var path = _files.GetOrAdd(name, n => Path.Combine(_baseDir, SanitizeFileName(n) + ".json"));
                    if (!File.Exists(path)) File.WriteAllText(path, "[]");
                    object result = true;
                    return Task.FromResult((TResult)result);
                }
            case global::Sora.Data.DataInstructions.Clear:
                {
                    var (name, store) = ResolveNameAndStore();
                    var deleted = store.Count;
                    store.Clear();
                    Persist(name, store);
                    object result = deleted;
                    return Task.FromResult((TResult)result);
                }
            default:
                throw new NotSupportedException($"Instruction '{instruction.Name}' not supported by JSON adapter for {typeof(TEntity).Name}.");
        }
    }

    private void LoadFromDisk(string path, ConcurrentDictionary<TKey, TEntity> store)
    {
        if (!File.Exists(path)) return;
        try
        {
            var json = File.ReadAllText(path);
            var list = JsonSerializer.Deserialize<List<TEntity>>(json, _json) ?? [];
            foreach (var item in list) store[item.Id] = item;
        }
        catch { /* ignore corrupted files in early dev */ }
    }

    private void Persist(string physicalName, ConcurrentDictionary<TKey, TEntity> store)
    {
        var path = _files.GetOrAdd(physicalName, n => Path.Combine(_baseDir, SanitizeFileName(n) + ".json"));
        var list = store.Values.ToList();
        var json = JsonSerializer.Serialize(list, _json);
        File.WriteAllText(path, json);
    }

    private ConcurrentDictionary<TKey, TEntity> ResolveStore()
    {
        var name = ComputePhysicalName();
        return _stores.GetOrAdd(name, n =>
        {
            var s = new ConcurrentDictionary<TKey, TEntity>();
            var path = Path.Combine(_baseDir, SanitizeFileName(n) + ".json");
            _files[n] = path;
            LoadFromDisk(path, s);
            return s;
        });
    }

    private (string name, ConcurrentDictionary<TKey, TEntity> store) ResolveNameAndStore()
    {
        var name = ComputePhysicalName();
        var store = ResolveStore();
        return (name, store);
    }

    private static string SanitizeFileName(string physicalName)
        => physicalName.Replace(':', '.');

    private static string ComputePhysicalName()
    {
        var sp = Sora.Core.SoraApp.Current;
        if (sp is not null)
        {
            // Delegate to central naming registry which is set-aware
            return Sora.Data.Core.Configuration.StorageNameRegistry.GetOrCompute<TEntity, TKey>(sp);
        }
        return typeof(TEntity).Name;
    }

    private sealed class JsonBatch : IBatchSet<TEntity, TKey>
    {
        private readonly JsonRepository<TEntity, TKey> _repo;
        private readonly List<TEntity> _adds = new();
        private readonly List<TEntity> _updates = new();
        private readonly List<TKey> _deletes = new();
        private readonly List<(TKey id, Action<TEntity> mutate)> _mutations = new();

        public JsonBatch(JsonRepository<TEntity, TKey> repo) => _repo = repo;

        public IBatchSet<TEntity, TKey> Add(TEntity entity) { _adds.Add(entity); return this; }
        public IBatchSet<TEntity, TKey> Update(TEntity entity) { _updates.Add(entity); return this; }
        public IBatchSet<TEntity, TKey> Delete(TKey id) { _deletes.Add(id); return this; }
        public IBatchSet<TEntity, TKey> Update(TKey id, Action<TEntity> mutate) { _mutations.Add((id, mutate)); return this; }
        public IBatchSet<TEntity, TKey> Clear() { _adds.Clear(); _updates.Clear(); _deletes.Clear(); _mutations.Clear(); return this; }

        public Task<BatchResult> SaveAsync(BatchOptions? options = null, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            // If strict atomic batches are required, JSON adapter cannot guarantee; follow contract and signal not supported
            if (options?.RequireAtomic == true)
                throw new NotSupportedException("JSON adapter does not support atomic batch transactions.");

            // apply in-memory mutations before persisting
            if (_mutations.Count != 0)
            {
                foreach (var (id, mutate) in _mutations)
                {
                    ct.ThrowIfCancellationRequested();
                    var curStore = _repo.ResolveStore();
                    if (curStore.TryGetValue(id, out var current))
                    {
                        mutate(current);
                        _updates.Add(current);
                    }
                }
            }
            var (name, store) = _repo.ResolveNameAndStore();
            foreach (var e in _adds) { store[e.Id] = e; ct.ThrowIfCancellationRequested(); }
            foreach (var e in _updates) { store[e.Id] = e; ct.ThrowIfCancellationRequested(); }
            var del = 0; foreach (var id in _deletes) { if (store.TryRemove(id, out _)) del++; ct.ThrowIfCancellationRequested(); }
            _repo.Persist(name, store);
            return Task.FromResult(new BatchResult(_adds.Count, _updates.Count, del));
        }
    }

    // Adapter stays simple; no auto-ID here by design.

    // No identifier generation here; RepositoryFacade ensures IDs cross-cutting.
}
