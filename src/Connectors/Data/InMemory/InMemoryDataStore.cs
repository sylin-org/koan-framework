using System.Collections.Concurrent;

namespace Koan.Data.Connector.InMemory;

/// <summary>
/// Thread-safe global storage manager for in-memory data.
/// Maintains isolated storage per (entity type, partition) tuple.
/// Singleton lifetime ensures data persists across repository instances.
/// </summary>
public sealed class InMemoryDataStore
{
    private readonly ConcurrentDictionary<StoreKey, object> _stores = new();

    /// <summary>
    /// Gets or creates a thread-safe store for the specified entity type and partition.
    /// </summary>
    public ConcurrentDictionary<TKey, TEntity> GetOrCreateStore<TEntity, TKey>(string partition)
        where TEntity : class
        where TKey : notnull
    {
        var key = new StoreKey(typeof(TEntity), partition);
        return (ConcurrentDictionary<TKey, TEntity>)_stores.GetOrAdd(
            key,
            _ => new ConcurrentDictionary<TKey, TEntity>()
        );
    }

    /// <summary>
    /// Clears all data for the specified entity type across all partitions.
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
    /// Clears all data across all entity types and partitions.
    /// Use with caution - primarily for testing scenarios.
    /// </summary>
    public void ClearAll()
    {
        _stores.Clear();
    }

    private readonly record struct StoreKey(Type EntityType, string Partition);
}
