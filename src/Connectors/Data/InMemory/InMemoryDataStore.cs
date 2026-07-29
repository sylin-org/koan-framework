using System.Collections.Concurrent;
using Koan.Data.Core.KeyValue;
using Koan.Data.Connector.InMemory.Infrastructure;

namespace Koan.Data.Connector.InMemory;

/// <summary>
/// Host-owned storage manager for in-memory data. Maintains an isolated store per
/// (routed source, entity type, partition) tuple — so Database mode (per source, ARCH-0103) and Container mode
/// (per partition) both isolate physically, with no external infrastructure. Each store holds
/// <see cref="KvRecord{TEntity}"/> envelopes (entity + stamped managed values), the object-graph family's sidecar that
/// lets the in-memory read-filter evaluate the managed discriminator without mutating the POCO. The module registers
/// one instance per host, so repositories share data without leaking a public reset mechanism or process-global state.
/// </summary>
internal sealed class InMemoryDataStore
{
    private readonly ConcurrentDictionary<StoreKey, Lazy<object>> _stores = new();
    private int _storeCount;

    /// <summary>
    /// Gets or creates a thread-safe store for the specified routed source, entity type, and partition.
    /// </summary>
    internal ConcurrentDictionary<TKey, KvRecord<TEntity>> GetOrCreateStore<TEntity, TKey>(string source, string partition)
        where TEntity : class
        where TKey : notnull
    {
        var key = new StoreKey(source, typeof(TEntity), partition);
        var store = _stores.GetOrAdd(
            key,
            _ => new Lazy<object>(
                CreateStore<TEntity, TKey>,
                LazyThreadSafetyMode.ExecutionAndPublication));
        return (ConcurrentDictionary<TKey, KvRecord<TEntity>>)store.Value;
    }

    private object CreateStore<TEntity, TKey>()
        where TEntity : class
        where TKey : notnull
    {
        var count = Interlocked.Increment(ref _storeCount);
        if (count <= Constants.Provider.MaximumStoresPerHost)
            return new ConcurrentDictionary<TKey, KvRecord<TEntity>>();
        Interlocked.Decrement(ref _storeCount);
        throw new InvalidOperationException(
            $"InMemory reached the host bound of {Constants.Provider.MaximumStoresPerHost} source/type/partition stores.");
    }

    private readonly record struct StoreKey(string Source, Type EntityType, string Partition);
}
