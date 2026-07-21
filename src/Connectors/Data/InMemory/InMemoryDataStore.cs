using System.Collections.Concurrent;
using Koan.Data.Core.KeyValue;

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
    private readonly ConcurrentDictionary<StoreKey, object> _stores = new();

    /// <summary>
    /// Gets or creates a thread-safe store for the specified routed source, entity type, and partition.
    /// </summary>
    internal ConcurrentDictionary<TKey, KvRecord<TEntity>> GetOrCreateStore<TEntity, TKey>(string source, string partition)
        where TEntity : class
        where TKey : notnull
    {
        var key = new StoreKey(source, typeof(TEntity), partition);
        return (ConcurrentDictionary<TKey, KvRecord<TEntity>>)_stores.GetOrAdd(
            key,
            _ => new ConcurrentDictionary<TKey, KvRecord<TEntity>>()
        );
    }

    private readonly record struct StoreKey(string Source, Type EntityType, string Partition);
}
