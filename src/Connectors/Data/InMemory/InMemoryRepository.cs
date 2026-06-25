using System.Collections.Concurrent;
using Koan.Core.Capabilities;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Core.KeyValue;

namespace Koan.Data.Connector.InMemory;

/// <summary>
/// In-memory key-value adapter — the zero-infrastructure floor and the cross-adapter convergence oracle. Built on the
/// <see cref="KeyValueStore{TEntity,TKey}"/> family base (ARCH-0103 §9), so it inherits all three AODB modes: Shared
/// (the object-graph sidecar — the store holds <see cref="KvRecord{TEntity}"/> envelopes so the managed discriminator is
/// filtered without mutating the POCO), Container (a distinct store per ambient partition), and Database (a distinct
/// store per routed source). This adapter supplies only the backend primitives over the shared
/// <see cref="InMemoryDataStore"/>; every contract (write-stamp, cross-scope guard, managed-aware read, batch,
/// instructions) lives in the base.
/// </summary>
internal sealed class InMemoryRepository<TEntity, TKey> : KeyValueStore<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly InMemoryDataStore _dataStore;
    private readonly string _source;

    public InMemoryRepository(InMemoryDataStore dataStore, string source)
    {
        _dataStore = dataStore;
        _source = source;
    }

    // The current physical store: per (routed source, entity type, ambient partition).
    private ConcurrentDictionary<TKey, KvRecord<TEntity>> Store()
    {
        var partition = Koan.Data.Core.EntityContext.Current?.Partition ?? "default";
        return _dataStore.GetOrCreateStore<TEntity, TKey>(_source, partition);
    }

    protected override Task<KvRecord<TEntity>?> ReadAsync(TKey id, CancellationToken ct)
        => Task.FromResult(Store().TryGetValue(id, out var r) ? r : (KvRecord<TEntity>?)null);

    protected override Task<IReadOnlyList<KvRecord<TEntity>>> ScanAsync(CancellationToken ct)
        => Task.FromResult((IReadOnlyList<KvRecord<TEntity>>)Store().Values.ToList());

    protected override Task WriteAsync(TKey id, KvRecord<TEntity> record, CancellationToken ct)
    {
        Store()[id] = record;
        return Task.CompletedTask;
    }

    protected override Task<bool> RemoveAsync(TKey id, CancellationToken ct)
        => Task.FromResult(Store().TryRemove(id, out _));

    protected override Task<int> ClearAsync(CancellationToken ct)
    {
        var store = Store();
        var count = store.Count;
        store.Clear();
        return Task.FromResult(count);
    }

    // The in-memory floor honours full LINQ-to-objects + atomic bulk writes.
    protected override void DescribeBackend(ICapabilities caps) => caps
        .Add(DataCaps.Write.BulkUpsert).Add(DataCaps.Write.BulkDelete).Add(DataCaps.Write.AtomicBatch);
}
