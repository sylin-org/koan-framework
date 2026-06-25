using System.Collections.Concurrent;
using Koan.Data.Core.KeyValue;

namespace Koan.Data.Connector.InMemory;

/// <summary>
/// Thread-safe global storage manager for in-memory data. Maintains an isolated store per
/// (routed source, entity type, partition) tuple — so Database mode (per source, ARCH-0103) and Container mode
/// (per partition) both isolate physically, with no external infrastructure. Each store holds
/// <see cref="KvRecord{TEntity}"/> envelopes (entity + stamped managed values), the object-graph family's sidecar that
/// lets the in-memory read-filter evaluate the managed discriminator without mutating the POCO. Singleton lifetime
/// ensures data persists across repository instances.
/// </summary>
public sealed class InMemoryDataStore
{
    private readonly ConcurrentDictionary<StoreKey, object> _stores = new();

    /// <summary>
    /// Gets or creates a thread-safe store for the specified routed source, entity type, and partition.
    /// </summary>
    public ConcurrentDictionary<TKey, KvRecord<TEntity>> GetOrCreateStore<TEntity, TKey>(string source, string partition)
        where TEntity : class
        where TKey : notnull
    {
        var key = new StoreKey(source, typeof(TEntity), partition);
        return (ConcurrentDictionary<TKey, KvRecord<TEntity>>)_stores.GetOrAdd(
            key,
            _ => new ConcurrentDictionary<TKey, KvRecord<TEntity>>()
        );
    }

    /// <summary>
    /// Clears all data for the specified entity type across all sources and partitions.
    /// Use with caution - primarily for testing scenarios.
    /// </summary>
    public void ClearAll<TEntity>() where TEntity : class
    {
        var entityType = typeof(TEntity);
        var keysToRemove = _stores.Keys.Where(k => k.EntityType == entityType).ToList();
        foreach (var key in keysToRemove)
        {
            _stores.TryRemove(key, out _);
        }
    }

    /// <summary>
    /// Clears all data across all entity types, sources, and partitions.
    /// Use with caution - primarily for testing scenarios.
    /// </summary>
    public void ClearAll()
    {
        _stores.Clear();
    }

    private readonly record struct StoreKey(string Source, Type EntityType, string Partition);
}
