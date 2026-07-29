using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Koan.Core.Capabilities;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Instructions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Core;
using Koan.Data.Core.KeyValue;
using Koan.Data.Core.Polymorphism;
using Koan.Data.Core.Semantics;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Connector.Json.Runtime;
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
    private readonly IReadOnlyList<DataSegmentationField> _segmentationFields;
    private readonly JsonRoute _route;
    private readonly string _baseDir;
    private readonly INamingProvider _naming;
    private readonly IServiceProvider _services;
    // Per-physical-name (partition) stores + file paths so different partitions are isolated within this source's dir.
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<TKey, KvRecord<TEntity>>> _stores = new();
    // One write-gate per physical file: the singleton repository serves every request, so concurrent writes to the same
    // partition would otherwise race File.WriteAllTextAsync to the same path (a sharing-violation IOException). The
    // in-memory store stays the read source of truth; this just serializes the write-through snapshots per file.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _writeGates = new();

    internal JsonRepository(
        JsonRoute route,
        DataSegmentationPlan segmentation,
        INamingProvider naming,
        IServiceProvider services)
    {
        _route = route;
        _baseDir = route.DirectoryPath;
        if (route.StorageLifecycle == StorageLifecycle.Managed && route.Access == DataSourceAccess.ReadWrite)
            Directory.CreateDirectory(_baseDir);
        else if (!Directory.Exists(_baseDir))
            throw new DirectoryNotFoundException(
                $"JSON source '{route.Source}' requires existing directory '{_baseDir}' for " +
                $"{route.StorageLifecycle}/{route.Access}; the adapter will not create it.");
        EntityJsonSerialization.Apply(_json);
        _segmentationFields = segmentation.For(typeof(TEntity)).Fields;
        _naming = naming;
        _services = services;
    }

    // ==================== Backend primitives ====================

    protected override Task<KvRecord<TEntity>?> ReadAsync(TKey id, CancellationToken ct)
    {
        var (_, store) = Resolve();
        if (!store.TryGetValue(id, out var record))
            return Task.FromResult<KvRecord<TEntity>?>(null);
        return Task.FromResult<KvRecord<TEntity>?>(Clone(record, JsonSerializer.Create(_json)));
    }

    protected override Task<IReadOnlyList<KvRecord<TEntity>>> ScanAsync(CancellationToken ct)
    {
        var (_, store) = Resolve();
        var serializer = JsonSerializer.Create(_json);
        return Task.FromResult((IReadOnlyList<KvRecord<TEntity>>)store.Values.Select(record => Clone(record, serializer)).ToList());
    }

    protected override Task<IReadOnlyList<KvRecord<TEntity>>> ScanBoundedAsync(int maxCandidates, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (_, store) = Resolve();
        var serializer = JsonSerializer.Create(_json);
        return Task.FromResult((IReadOnlyList<KvRecord<TEntity>>)store.Values
            .Take(maxCandidates)
            .Select(record => Clone(record, serializer))
            .ToList());
    }

    protected override async Task WriteAsync(TKey id, KvRecord<TEntity> record, CancellationToken ct)
    {
        await Commit(
            candidate => { candidate[id] = record; return true; },
            static _ => true,
            ct).ConfigureAwait(false);
    }

    protected override async Task<bool> RemoveAsync(TKey id, CancellationToken ct)
    {
        return await Commit(
            candidate => candidate.Remove(id),
            static changed => changed,
            ct).ConfigureAwait(false);
    }

    // Bulk write/remove collapse to ONE file persist (the base default would persist per row — O(N²) file rewrites).
    protected override async Task WriteManyAsync(IReadOnlyList<KvRecord<TEntity>> records, CancellationToken ct)
    {
        if (records.Count == 0) return;
        await Commit(
            candidate =>
            {
                foreach (var record in records) candidate[record.Entity.Id] = record;
                return true;
            },
            static _ => true,
            ct).ConfigureAwait(false);
    }

    protected override async Task<int> RemoveManyAsync(IReadOnlyList<TKey> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return 0;
        return await Commit(
            candidate =>
            {
                var count = 0;
                foreach (var id in ids) if (candidate.Remove(id)) count++;
                return count;
            },
            static count => count > 0,
            ct).ConfigureAwait(false);
    }

    protected override async Task<int> ClearAsync(CancellationToken ct)
    {
        return await Commit(
            candidate =>
            {
                var count = candidate.Count;
                candidate.Clear();
                return count;
            },
            static _ => true,
            ct).ConfigureAwait(false);
    }

    // JSON is a file floor — no native bulk / atomic APIs to announce. The family caps (LINQ, Full filter, RowScoped)
    // come from the base's Describe.
    protected override void DescribeBackend(ICapabilities caps) => JsonFeatures.DescribeBackend(caps);

    // ==================== Instructions ====================

    public override async Task<TResult> ExecuteAsync<TResult>(Instruction instruction, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        ct.ThrowIfCancellationRequested();
        if (instruction.Name == DataInstructions.EnsureCreated)
        {
            if (_route.StorageLifecycle == StorageLifecycle.External)
            {
                _ = Resolve();
                return (TResult)(object)true;
            }
            DemandWrite("ensure storage");
            // Prepare the source directory + touch the current partition's set file so presence checks see "[]".
            Directory.CreateDirectory(_baseDir);
            await Commit(static _ => true, static _ => true, ct).ConfigureAwait(false);
            return (TResult)(object)true;
        }
        return await base.ExecuteAsync<TResult>(instruction, ct).ConfigureAwait(false);
    }

    // ==================== On-disk store resolution + serialization ====================

    private (string name, ConcurrentDictionary<TKey, KvRecord<TEntity>> store) Resolve()
    {
        var name = ComputePhysicalName();
        DemandCapacity(name);
        var store = _stores.GetOrAdd(name, n =>
        {
            var s = new ConcurrentDictionary<TKey, KvRecord<TEntity>>();
            var path = Path.Combine(_baseDir, SanitizeFileName(n) + ".json");
            if (_route.StorageLifecycle == StorageLifecycle.External && !File.Exists(path))
                throw new FileNotFoundException(
                    $"External JSON container '{path}' does not exist; the adapter will not create it.",
                    path);
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
            var serializer = JsonSerializer.Create(_json);
            foreach (var token in arr)
            {
                if (token is not JObject jo)
                    throw new JsonSerializationException("Every JSON store item must be an object.");
                // Extract the managed __-keys back into the envelope's sidecar (null off-axis), then deserialize the
                // entity (it ignores the unknown __-keys, exactly as the relational read does).
                var managed = ManagedFieldJsonInjector.ExtractManaged(jo, typeof(TEntity), _segmentationFields);
                var entity = (TEntity)EntityJsonSerialization.MaterializeStored(jo, typeof(TEntity), serializer);
                if (entity is null) continue;
                store[entity.Id] = new KvRecord<TEntity>(entity, managed);
            }
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                $"Koan JSON could not read '{path}' because it does not contain a valid entity store. " +
                "Restore the file from a known-good copy or remove it deliberately; corrupt storage is never treated as empty.",
                ex);
        }
    }

    private async Task<TResult> Commit<TResult>(
        Func<Dictionary<TKey, KvRecord<TEntity>>, TResult> mutate,
        Func<TResult, bool> shouldPersist,
        CancellationToken ct)
    {
        DemandWrite("persist entity data");
        var physicalName = ComputePhysicalName();
        DemandCapacity(physicalName);
        var gate = _writeGates.GetOrAdd(physicalName, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var current = Resolve().store;
            var candidate = current.ToDictionary(static item => item.Key, static item => item.Value);
            var result = mutate(candidate);
            if (!shouldPersist(result)) return result;
            await PersistSnapshot(physicalName, candidate.Values, ct).ConfigureAwait(false);
            var serializer = JsonSerializer.Create(_json);
            _stores[physicalName] = new ConcurrentDictionary<TKey, KvRecord<TEntity>>(
                candidate.Select(item => new KeyValuePair<TKey, KvRecord<TEntity>>(
                    item.Key,
                    Clone(item.Value, serializer))));
            return result;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task PersistSnapshot(
        string physicalName,
        IEnumerable<KvRecord<TEntity>> records,
        CancellationToken ct)
    {
        var serializer = JsonSerializer.Create(_json);
        var arr = new JArray();
        foreach (var record in records)
        {
            var document = JObject.FromObject(record.Entity, serializer);
            ManagedFieldJsonInjector.InjectManaged(document, record.Managed);
            arr.Add(document);
        }
        var path = Path.Combine(_baseDir, SanitizeFileName(physicalName) + ".json");
        if (_route.StorageLifecycle == StorageLifecycle.External && !File.Exists(path))
            throw new FileNotFoundException(
                $"External JSON container '{path}' does not exist; the adapter will not create it.",
                path);
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(temporaryPath, arr.ToString(Formatting.None), ct).ConfigureAwait(false);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private void DemandCapacity(string physicalName)
    {
        if (_stores.ContainsKey(physicalName) || _stores.Count < Infrastructure.Constants.Provider.MaximumFilesPerRepository)
            return;
        throw new InvalidOperationException(
            $"JSON reached the repository bound of {Infrastructure.Constants.Provider.MaximumFilesPerRepository} physical files.");
    }

    private static KvRecord<TEntity> Clone(KvRecord<TEntity> record, JsonSerializer serializer)
    {
        var document = JObject.FromObject(record.Entity, serializer);
        var entity = (TEntity)EntityJsonSerialization.MaterializeStored(document, typeof(TEntity), serializer);
        var managed = record.Managed is null
            ? null
            : new Dictionary<string, object?>(record.Managed, StringComparer.Ordinal);
        return new KvRecord<TEntity>(entity, managed);
    }

    private void DemandWrite(string operation)
    {
        if (_route.Access == DataSourceAccess.ReadWrite) return;
        throw new DataSourcePolicyException(
            _route.Source,
            operation,
            DataOperationEffect.Write,
            _route.StorageLifecycle,
            _route.Access,
            DataSourcePolicyException.PolicyDeniedCode,
            "Select Access=ReadWrite before writing JSON data.");
    }

    private static string SanitizeFileName(string physicalName)
        => physicalName.Replace(':', '.');

    private string ComputePhysicalName()
        => _naming.ResolveStorage(
            typeof(TEntity),
            EntityContext.Current?.Partition,
            _services);
}
