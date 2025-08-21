using Sora.Data.Abstractions;
using Sora.Data.Vector.Abstractions;

namespace Sora.Data.Core;

public static class DataServiceVectorExtensions
{
    /// <summary>
    /// Get a vector repository for the specified aggregate and key type, or throw with a helpful message if none is configured.
    /// </summary>
    public static Sora.Data.Vector.Abstractions.IVectorSearchRepository<TEntity, TKey> GetRequiredVectorRepository<TEntity, TKey>(this IDataService data)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var repo = data.TryGetVectorRepository<TEntity, TKey>();
        if (repo is null)
            throw new InvalidOperationException($"No vector adapter is configured for {typeof(TEntity).Name}<{typeof(TKey).Name}>. Ensure a vector adapter package is referenced and registers an IVectorAdapterFactory for the current provider.");
        return repo;
    }
}
