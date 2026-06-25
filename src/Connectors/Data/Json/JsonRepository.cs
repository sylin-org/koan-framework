using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Koan.Core.Capabilities;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Instructions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Core;
using Koan.Data.Core.KeyValue;
using System.Collections.Concurrent;

namespace Koan.Data.Connector.Json;

/// <summary>
/// JSON file-backed key-value adapter — an in-memory dictionary with per-aggregate JSON file persistence, built on the
/// <see cref="KeyValueStore{TEntity,TKey}"/> family base (ARCH-0103 §9, the JSON-text family). It inherits all three
/// AODB modes: <b>Shared</b> (the managed discriminator is injected into / extracted from the persisted JSON via the
/// shared <see cref="ManagedFieldJsonInjector"/> — the same write-stamp the relational trio uses — and the base's hybrid
/// evaluator filters on it), <b>Container</b> (a distinct JSON file per ambient partition), and <b>Database</b> (a
/// distinct directory per routed source, resolved by <see cref="JsonAdapterFactory"/>). This adapter supplies only the
/// backend primitives over its on-disk stores; every contract (write-stamp, cross-scope guard, managed-aware read,
/// batch, instructions) lives in the base.
/// </summary>
internal sealed class JsonRepository<TEntity, TKey> : KeyValueStore<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    // CamelCase entity body (byte-compatible with the pre-rebuild on-disk form); the managed __-keys ride alongside it
    // via the shared injector, written with their literal storage names (leading '_' is a camel-case fixed point).
    private readonly JsonSerializerSettings _json = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        Formatting = Formatting.None,
        NullValueHandling = NullValueHandling.Include,
    };
    private readonly JsonSerializer _serializer;
    private readonly string _baseDir;
    // Per-physical-name (partition) stores + file paths so different partitions are isolated within this source's dir.
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<TKey, KvRecord<TEntity>>> _stores = new();
    private readonly ConcurrentDictionary<string, string> _files = new();
    // One write-gate per physical file: the singleton repository serves every request, so concurrent writes to the same
    // partition would otherwise race File.WriteAllTextAsync to the same path (a sharing-violation IOException). The
    // in-memory store stays the read source of truth; this just serializes the write-through snapshots per file.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _writeGates = new();

    public JsonRepository(IOptions<JsonDataOptions> options)
    {
        _baseDir = options.Value.DirectoryPath;
        Directory.CreateDirectory(_baseDir);
        _serializer = JsonSerializer.Create(_json);
    }

    // ==================== Backend primitives ====================

    protected override Task<KvRecord<TEntity>?> ReadAsync(TKey id, CancellationToken ct)
    {
        var (_, store) = Resolve();
        return Task.FromResult(store.TryGetValue(id, out var r) ? r : (KvRecord<TEntity>?)null);
    }

    protected override Task<IReadOnlyList<KvRecord<TEntity>>> ScanAsync(CancellationToken ct)
    {
        var (_, store) = Resolve();
        return Task.FromResult((IReadOnlyList<KvRecord<TEntity>>)store.Values.ToList());
    }

    protected override async Task WriteAsync(TKey id, KvRecord<TEntity> record, CancellationToken ct)
    {
        var (name, store) = Resolve();
        store[id] = record;
        await PersistAsync(name, store).ConfigureAwait(false);
    }

    protected override async Task<bool> RemoveAsync(TKey id, CancellationToken ct)
    {
        var (name, store) = Resolve();
        var ok = store.TryRemove(id, out _);
        if (ok) await PersistAsync(name, store).ConfigureAwait(false);
        return ok;
    }

    protected override async Task<int> ClearAsync(CancellationToken ct)
    {
        var (name, store) = Resolve();
        var count = store.Count;
        store.Clear();
        await PersistAsync(name, store).ConfigureAwait(false);   // an empty store persists as "[]"
        return count;
    }

    // JSON is a file floor — no native bulk / atomic APIs to announce. The family caps (LINQ, Full filter, RowScoped)
    // come from the base's Describe.
    protected override void DescribeBackend(ICapabilities caps) { }

    // ==================== Instructions ====================

    public override async Task<TResult> ExecuteAsync<TResult>(Instruction instruction, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        ct.ThrowIfCancellationRequested();
        if (instruction.Name == DataInstructions.EnsureCreated)
        {
            // Prepare the source directory + touch the current partition's set file so presence checks see "[]".
            Directory.CreateDirectory(_baseDir);
            var (name, store) = Resolve();
            await PersistAsync(name, store).ConfigureAwait(false);
            return (TResult)(object)true;
        }
        return await base.ExecuteAsync<TResult>(instruction, ct).ConfigureAwait(false);
    }

    // ==================== On-disk store resolution + serialization ====================

    private (string name, ConcurrentDictionary<TKey, KvRecord<TEntity>> store) Resolve()
    {
        var name = ComputePhysicalName();
        var store = _stores.GetOrAdd(name, n =>
        {
            var s = new ConcurrentDictionary<TKey, KvRecord<TEntity>>();
            var path = Path.Combine(_baseDir, SanitizeFileName(n) + ".json");
            _files[n] = path;
            LoadFromDisk(path, s);
            return s;
        });
        return (name, store);
    }

    private void LoadFromDisk(string path, ConcurrentDictionary<TKey, KvRecord<TEntity>> store)
    {
        if (!File.Exists(path)) return;
        try
        {
            var json = File.ReadAllText(path);
            var arr = JArray.Parse(json);
            foreach (var token in arr)
            {
                if (token is not JObject jo) continue;
                // Extract the managed __-keys back into the envelope's sidecar (null off-axis), then deserialize the
                // entity (it ignores the unknown __-keys, exactly as the relational read does).
                var managed = ManagedFieldJsonInjector.ExtractManaged(jo, typeof(TEntity));
                var entity = jo.ToObject<TEntity>(_serializer);
                if (entity is null) continue;
                store[entity.Id] = new KvRecord<TEntity>(entity, managed);
            }
        }
        catch { /* ignore corrupted files in early dev */ }
    }

    private async Task PersistAsync(string physicalName, ConcurrentDictionary<TKey, KvRecord<TEntity>> store)
    {
        var path = _files.GetOrAdd(physicalName, n => Path.Combine(_baseDir, SanitizeFileName(n) + ".json"));
        var gate = _writeGates.GetOrAdd(physicalName, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var arr = new JArray();
            foreach (var rec in store.Values)
            {
                var jo = JObject.FromObject(rec.Entity, _serializer);
                // Stamp the record's own managed values onto the JSON (write-stamp); a null/empty sidecar adds nothing,
                // so the off-axis bytes stay identical to a plain entity array.
                ManagedFieldJsonInjector.InjectManaged(jo, rec.Managed);
                arr.Add(jo);
            }
            await File.WriteAllTextAsync(path, arr.ToString(Formatting.None)).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private static string SanitizeFileName(string physicalName)
        => physicalName.Replace(':', '.');

    private static string ComputePhysicalName()
    {
        var sp = Koan.Core.Hosting.App.AppHost.Current;
        if (sp is not null)
        {
            // Delegate to adapter naming (factory owns the cache and partition composition).
            return Core.Configuration.AdapterNaming.GetOrCompute<TEntity, TKey>(sp);
        }
        // No host yet (early init / direct construction): still route through the resolver with this adapter's
        // announced convention so a generic entity name is grammar-correct (not the mangled typeof(T).Name).
        return StorageNameResolver.Resolve(typeof(TEntity),
            new StorageNameResolver.Convention(StorageNamingStyle.EntityType, "_", NameCasing.AsIs));
    }
}
