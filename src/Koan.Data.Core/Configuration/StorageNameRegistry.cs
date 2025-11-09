using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;

namespace Koan.Data.Core.Configuration;

/// <summary>
/// Central registry to resolve and cache storage names per (entity, key) into AggregateBags.
/// </summary>
public static class StorageNameRegistry
{
    private static string CacheKey(string provider, string? partition)
    {
        var trimmedPartition = partition?.Trim();

        return string.IsNullOrEmpty(trimmedPartition)
            ? $"name:{provider}"
            : $"name:{provider}:{trimmedPartition}";
    }

    public static string GetOrCompute<TEntity, TKey>(IServiceProvider sp)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var cfg = AggregateConfigs.Get<TEntity, TKey>(sp);
        var provider = cfg.Provider;
        var partition = EntityContext.Current?.Partition;
        var key = CacheKey(provider, partition);
        return AggregateBags.GetOrAdd<TEntity, TKey, string>(sp, key, () =>
        {
            var namingProvider = ResolveProvider(sp, provider);
            return NamingComposer.Compose(namingProvider, typeof(TEntity), partition, sp);
        });
    }

    private static INamingProvider ResolveProvider(IServiceProvider sp, string providerKey)
    {
        // Query ONLY data adapter factories (vector has its own registry)
        var factories = sp.GetServices<IDataAdapterFactory>();

        foreach (var factory in factories)
        {
            if (factory.CanHandle(providerKey))
                return factory;
        }

        throw new InvalidOperationException(
            $"No data adapter registered for provider '{providerKey}'. " +
            $"Ensure an IDataAdapterFactory implementation is registered for this provider.");
    }
}
