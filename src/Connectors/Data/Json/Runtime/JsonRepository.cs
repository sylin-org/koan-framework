using System.Text;
using Koan.Core.Capabilities;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Instructions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Core;
using Koan.Data.Core.KeyValue;
using Koan.Data.Core.Polymorphism;
using Koan.Data.Core.Semantics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Koan.Data.Connector.Json.Runtime;

/// <summary>Translates the KeyValue family primitives to bounded, durable JSON file snapshots.</summary>
internal sealed class JsonRepository<TEntity, TKey> : KeyValueStore<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private static readonly Type RootType = EntityRootDescriptor.For(typeof(TEntity)).RootType;
    private static readonly JsonSerializerSettings Settings = EntityJsonSerialization.Apply(new JsonSerializerSettings
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        Formatting = Formatting.None,
        NullValueHandling = NullValueHandling.Include
    });
    private static readonly Encoding Utf8 = new UTF8Encoding(false);

    private readonly JsonRoute _route;
    private readonly JsonFileRegistry _files;
    private readonly IReadOnlyList<DataSegmentationField> _segmentation;
    private readonly INamingProvider _naming;
    private readonly IServiceProvider _services;

    internal JsonRepository(
        JsonRoute route,
        JsonFileRegistry files,
        DataSegmentationPlan segmentation,
        INamingProvider naming,
        IServiceProvider services)
    {
        _route = route;
        _files = files;
        _segmentation = segmentation.For(RootType).Fields;
        _naming = naming;
        _services = services;
    }

    protected override Task<KvRecord<TEntity>?> ReadAsync(TKey id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var path = CurrentPath();
        if (TrySnapshot(path, out var snapshot))
            return Task.FromResult<KvRecord<TEntity>?>(
                snapshot.TryGetValue(id, out var json) ? Materialize(json) : null);
        return ReadSlow(path, id, ct);
    }

    protected override Task<IReadOnlyList<KvRecord<TEntity>>> ScanAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var path = CurrentPath();
        return TrySnapshot(path, out var snapshot)
            ? Task.FromResult(MaterializeAll(snapshot, int.MaxValue, ct))
            : ScanSlow(path, int.MaxValue, ct);
    }

    protected override Task<IReadOnlyList<KvRecord<TEntity>>> ScanBoundedAsync(
        int maxCandidates,
        CancellationToken ct)
    {
        if (maxCandidates <= 0) throw new ArgumentOutOfRangeException(nameof(maxCandidates));
        ct.ThrowIfCancellationRequested();
        var path = CurrentPath();
        return TrySnapshot(path, out var snapshot)
            ? Task.FromResult(MaterializeAll(snapshot, maxCandidates, ct))
            : ScanSlow(path, maxCandidates, ct);
    }

    protected override Task WriteAsync(TKey id, KvRecord<TEntity> record, CancellationToken ct)
    {
        var encoded = Encode(record);
        return Commit(
            candidate =>
            {
                candidate[id] = encoded;
                return true;
            },
            static _ => true,
            ct);
    }

    protected override Task<bool> RemoveAsync(TKey id, CancellationToken ct) => Commit(
        candidate => candidate.Remove(id),
        static changed => changed,
        ct);

    protected override Task WriteManyAsync(IReadOnlyList<KvRecord<TEntity>> records, CancellationToken ct)
    {
        if (records.Count == 0) return Task.CompletedTask;
        var encoded = new (TKey Id, string Json)[records.Count];
        for (var i = 0; i < records.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            encoded[i] = (records[i].Entity.Id, Encode(records[i]));
        }

        return Commit(
            candidate =>
            {
                foreach (var record in encoded) candidate[record.Id] = record.Json;
                return true;
            },
            static _ => true,
            ct);
    }

    protected override Task<int> RemoveManyAsync(IReadOnlyList<TKey> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return Task.FromResult(0);
        return Commit(
            candidate =>
            {
                var removed = 0;
                foreach (var id in ids)
                {
                    ct.ThrowIfCancellationRequested();
                    if (candidate.Remove(id)) removed++;
                }
                return removed;
            },
            static removed => removed > 0,
            ct);
    }

    protected override Task<int> ClearAsync(CancellationToken ct) => Commit(
        candidate =>
        {
            var removed = candidate.Count;
            candidate.Clear();
            return removed;
        },
        static _ => true,
        ct);

    protected override void DescribeBackend(ICapabilities capabilities) =>
        JsonFeatures.DescribeBackend(capabilities);

    public override async Task<TResult> ExecuteAsync<TResult>(Instruction instruction, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        ct.ThrowIfCancellationRequested();
        if (instruction.Name != DataInstructions.EnsureCreated)
            return await base.ExecuteAsync<TResult>(instruction, ct).ConfigureAwait(false);

        var path = CurrentPath();
        if (_route.Policy.StorageLifecycle == StorageLifecycle.External)
        {
            _ = await Snapshot(path, ct).ConfigureAwait(false);
            return (TResult)(object)true;
        }

        _route.Policy.Demand(DataOperationEffect.SchemaOrAdmin, "ensure JSON entity storage");
        var slot = _files.Get(path);
        await slot.Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var snapshot = SnapshotLocked(slot);
            await Persist(path, Records(snapshot), ct).ConfigureAwait(false);
            slot.Publish(snapshot);
        }
        finally
        {
            slot.Gate.Release();
        }
        return (TResult)(object)true;
    }

    private async Task<KvRecord<TEntity>?> ReadSlow(string path, TKey id, CancellationToken ct)
    {
        var snapshot = Records(await Snapshot(path, ct).ConfigureAwait(false));
        return snapshot.TryGetValue(id, out var json) ? Materialize(json) : null;
    }

    private async Task<IReadOnlyList<KvRecord<TEntity>>> ScanSlow(
        string path,
        int maximum,
        CancellationToken ct) =>
        MaterializeAll(Records(await Snapshot(path, ct).ConfigureAwait(false)), maximum, ct);

    private async Task<TResult> Commit<TResult>(
        Func<Dictionary<TKey, string>, TResult> mutate,
        Func<TResult, bool> shouldPersist,
        CancellationToken ct)
    {
        _route.Policy.Demand(DataOperationEffect.Write, "persist JSON entity data");
        ct.ThrowIfCancellationRequested();
        var path = CurrentPath();
        if (_route.Policy.StorageLifecycle == StorageLifecycle.External && !File.Exists(path))
        {
            throw new FileNotFoundException(
                $"External JSON entity file '{path}' does not exist; Koan will not create it.",
                path);
        }

        var slot = _files.Get(path);
        await slot.Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var current = SnapshotLocked(slot);
            var candidate = new Dictionary<TKey, string>(Records(current));
            var result = mutate(candidate);
            if (!shouldPersist(result)) return result;

            await Persist(path, candidate, ct).ConfigureAwait(false);
            slot.Publish(new JsonFileSnapshot(RootType, typeof(TKey), candidate));
            return result;
        }
        finally
        {
            slot.Gate.Release();
        }
    }

    private async Task<JsonFileSnapshot> Snapshot(string path, CancellationToken ct)
    {
        if (_files.TryGet(path, out var warm) && warm.Snapshot is { } ready)
            return DemandCompatible(ready, path);

        DemandReadable(path);
        var slot = _files.Get(path);
        await slot.Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return SnapshotLocked(slot);
        }
        finally
        {
            slot.Gate.Release();
        }
    }

    private JsonFileSnapshot SnapshotLocked(JsonFileSlot slot)
    {
        if (slot.Snapshot is { } ready) return DemandCompatible(ready, slot.Path);

        DemandReadable(slot.Path);
        var loaded = Load(slot.Path);
        slot.Publish(loaded);
        return loaded;
    }

    private JsonFileSnapshot Load(string path)
    {
        if (!File.Exists(path))
            return new JsonFileSnapshot(RootType, typeof(TKey), new Dictionary<TKey, string>());

        var length = new FileInfo(path).Length;
        if (length > Infrastructure.Constants.Provider.MaximumFileBytes)
        {
            throw new InvalidDataException(
                $"Koan JSON entity file '{path}' is {length} bytes and exceeds the 64 MiB safety bound. " +
                "Split the store or use a database adapter before reading it.");
        }

        try
        {
            var array = JArray.Parse(File.ReadAllText(path, Utf8));
            var records = new Dictionary<TKey, string>(array.Count);
            var serializer = JsonSerializer.Create(Settings);
            foreach (var token in array)
            {
                if (token is not JObject document)
                    throw new JsonSerializationException("Every JSON store item must be an object.");
                var entity = EntityJsonSerialization.MaterializeStored(document, typeof(TEntity), serializer) as TEntity
                    ?? throw new JsonSerializationException(
                        $"A JSON store item is not assignable to '{RootType.FullName}'.");
                if (!records.TryAdd(entity.Id, document.ToString(Formatting.None)))
                {
                    throw new InvalidDataException(
                        $"Koan JSON entity file '{path}' contains duplicate identity '{entity.Id}'. " +
                        "Repair the duplicate deliberately; ambiguous storage is never accepted.");
                }
            }
            return new JsonFileSnapshot(RootType, typeof(TKey), records);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw Corrupt(path, exception);
        }
    }

    private async Task Persist(string path, IReadOnlyDictionary<TKey, string> records, CancellationToken ct)
    {
        if (_route.Policy.StorageLifecycle == StorageLifecycle.External && !File.Exists(path))
        {
            throw new FileNotFoundException(
                $"External JSON entity file '{path}' does not exist; Koan will not create it.",
                path);
        }

        var payload = BuildPayload(records.Values);
        var bytes = Utf8.GetByteCount(payload);
        if (bytes > Infrastructure.Constants.Provider.MaximumFileBytes)
        {
            throw new InvalidOperationException(
                $"JSON write for '{path}' would be {bytes} bytes and exceeds the 64 MiB safety bound. " +
                "Split the store or use a database adapter.");
        }

        if (_route.Policy.StorageLifecycle == StorageLifecycle.Managed)
            Directory.CreateDirectory(_route.DirectoryPath);

        var temporary = $"{path}.{Guid.CreateVersion7():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, payload, Utf8, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private bool TrySnapshot(string path, out IReadOnlyDictionary<TKey, string> records)
    {
        if (_files.TryGet(path, out var slot) && slot.Snapshot is { } snapshot)
        {
            records = Records(DemandCompatible(snapshot, path));
            return true;
        }
        records = null!;
        return false;
    }

    private JsonFileSnapshot DemandCompatible(JsonFileSnapshot snapshot, string path)
    {
        if (snapshot.RootType == RootType && snapshot.KeyType == typeof(TKey) &&
            snapshot.Records is IReadOnlyDictionary<TKey, string>)
            return snapshot;

        throw new InvalidDataException(
            $"JSON entity file '{path}' is already owned by root '{snapshot.RootType.FullName}' with key " +
            $"'{snapshot.KeyType.FullName}', not '{RootType.FullName}'/'{typeof(TKey).FullName}'. " +
            "Choose distinct storage names for distinct Entity roots.");
    }

    private static IReadOnlyDictionary<TKey, string> Records(JsonFileSnapshot snapshot) =>
        (IReadOnlyDictionary<TKey, string>)snapshot.Records;

    private string Encode(KvRecord<TEntity> record)
    {
        var document = JObject.FromObject(record.Entity, JsonSerializer.Create(Settings));
        ManagedFieldJsonInjector.InjectManaged(document, record.Managed);
        return document.ToString(Formatting.None);
    }

    private KvRecord<TEntity> Materialize(string json)
    {
        try
        {
            var document = JObject.Parse(json);
            var managed = ManagedFieldJsonInjector.ExtractManaged(document, typeof(TEntity), _segmentation);
            var entity = EntityJsonSerialization.MaterializeStored(
                document,
                typeof(TEntity),
                JsonSerializer.Create(Settings)) as TEntity
                ?? throw new InvalidDataException(
                    $"JSON record could not materialize Entity root '{RootType.FullName}'.");
            return new KvRecord<TEntity>(entity, managed);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"A live JSON snapshot for '{RootType.FullName}' became invalid.",
                exception);
        }
    }

    private IReadOnlyList<KvRecord<TEntity>> MaterializeAll(
        IReadOnlyDictionary<TKey, string> snapshot,
        int maximum,
        CancellationToken ct)
    {
        var result = new List<KvRecord<TEntity>>(Math.Min(snapshot.Count, maximum));
        foreach (var record in snapshot)
        {
            ct.ThrowIfCancellationRequested();
            if (result.Count == maximum) break;
            result.Add(Materialize(record.Value));
        }
        return result;
    }

    private void DemandReadable(string path)
    {
        if (_route.Policy.StorageLifecycle == StorageLifecycle.External)
        {
            if (!Directory.Exists(_route.DirectoryPath))
                throw new DirectoryNotFoundException(
                    $"External JSON source '{_route.Source}' requires existing directory '{_route.DirectoryPath}'; Koan will not create it.");
            if (!File.Exists(path))
                throw new FileNotFoundException(
                    $"External JSON entity file '{path}' does not exist; Koan will not create it.",
                    path);
            return;
        }

        if (_route.Policy.Access == DataSourceAccess.ReadOnly && !Directory.Exists(_route.DirectoryPath))
        {
            throw new DirectoryNotFoundException(
                $"Read-only JSON source '{_route.Source}' requires existing directory '{_route.DirectoryPath}'; Koan will not create it.");
        }
    }

    private string CurrentPath() => _route.FileFor(_naming.ResolveStorage(
        RootType,
        EntityContext.Current?.Partition,
        _services));

    private static string BuildPayload(IEnumerable<string> records)
    {
        var payload = new StringBuilder("[");
        var separator = false;
        foreach (var record in records)
        {
            if (separator) payload.Append(',');
            payload.Append(record);
            separator = true;
        }
        return payload.Append(']').ToString();
    }

    private static InvalidDataException Corrupt(string path, Exception exception) => new(
        $"Koan JSON could not read '{path}' because it does not contain a valid entity store. " +
        "Restore the file from a known-good copy or remove it deliberately; corrupt storage is never treated as empty.",
        exception);
}
