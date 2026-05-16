using System;
using System.Collections.Concurrent;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;

namespace Koan.Data.Vector.Abstractions.Configuration;

/// <summary>
/// Central registry for vector collection/index names.
/// Parallel to StorageNameRegistry but for vector layer.
/// Caches resolved names per (entity, provider, partition).
/// </summary>
public static class VectorStorageNameRegistry
{
    private static readonly ConcurrentDictionary<string, string> Cache = new();

    /// <summary>
    /// Get or compute vector storage name for entity with optional partition.
    /// Uses VectorConfigs to resolve provider, then composes name via NamingComposer.
    /// </summary>
    /// <typeparam name="TEntity">Entity type</typeparam>
    /// <typeparam name="TKey">Entity key type</typeparam>
    /// <param name="sp">Service provider for dependency resolution</param>
    /// <returns>Fully composed vector collection/index name</returns>
    public static string GetOrCompute<TEntity, TKey>(IServiceProvider sp)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var cfg = VectorConfigs.Get<TEntity, TKey>(sp);
        var provider = cfg.Provider;
        var partition = Koan.Data.Core.EntityContext.Current?.Partition;
        var key = CacheKey(provider, partition);

        return Cache.GetOrAdd(key, _ =>
        {
            var namingProvider = ResolveProvider(sp, provider);
            return NamingComposer.Compose(namingProvider, typeof(TEntity), partition, sp);
        });
    }

    /// <summary>
    /// Generate cache key from provider and partition.
    /// Format: "vector:{provider}" or "vector:{provider}:{partition}"
    /// Prefix prevents collision with data layer cache if ever shared.
    /// </summary>
    private static string CacheKey(string provider, string? partition)
    {
        var trimmedPartition = partition?.Trim();
        return string.IsNullOrEmpty(trimmedPartition)
            ? $"vector:{provider}"
            : $"vector:{provider}:{trimmedPartition}";
    }

    /// <summary>
    /// Resolve naming provider (IVectorAdapterFactory) for given provider key.
    /// Queries only vector adapter factories (never data factories).
    /// </summary>
    private static INamingProvider ResolveProvider(IServiceProvider sp, string providerKey)
    {
        var factories = sp.GetServices<IVectorAdapterFactory>();

        foreach (var factory in factories)
        {
            if (factory.CanHandle(providerKey))
                return factory;
        }

        throw new InvalidOperationException(
            $"No vector adapter registered for provider '{providerKey}'. " +
            $"Ensure IVectorAdapterFactory implementation is registered for this provider.");
    }

    /// <summary>
    /// Clear cached names. Used for testing/host reset.
    /// </summary>
    internal static void Reset() => Cache.Clear();
}
