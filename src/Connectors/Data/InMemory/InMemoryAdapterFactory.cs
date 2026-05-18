using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Connector.InMemory;

/// <summary>
/// Data adapter factory for in-memory storage.
/// Priority: -100 (lowest) to act as fallback when no other adapter is configured.
/// </summary>
[ProviderPriority(-100)]
public sealed class InMemoryAdapterFactory : IDataAdapterFactory
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(System.Type, string?), string> _nameCache = new();

    public string Provider => "inmemory";

    public bool CanHandle(string provider) =>
        string.Equals(provider, "inmemory", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(provider, "memory", StringComparison.OrdinalIgnoreCase);

    public IDataRepository<TEntity, TKey> Create<TEntity, TKey>(
        IServiceProvider sp,
        string source = "Default")
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        // Get singleton data store
        var dataStore = sp.GetRequiredService<InMemoryDataStore>();

        // Resolve partition from EntityContext or use default
        var partition = GetPartition();

        return new InMemoryRepository<TEntity, TKey>(dataStore, partition);
    }

    private static string GetPartition()
    {
        var ctx = Koan.Data.Core.EntityContext.Current;
        if (ctx?.Partition != null)
            return ctx.Partition;

        // Default partition
        return "default";
    }

    public string ResolveStorage(Type entityType, string? partition, IServiceProvider services)
    {
        var trimmed = partition?.Trim();
        var cacheKey = (entityType, string.IsNullOrEmpty(trimmed) ? null : trimmed);
        return _nameCache.GetOrAdd(cacheKey, _ =>
            string.IsNullOrEmpty(trimmed) ? entityType.Name : entityType.Name + "#" + trimmed);
    }
}
