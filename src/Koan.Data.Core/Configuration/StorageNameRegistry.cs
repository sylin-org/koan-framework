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
            return GetTargetRepositoryName<TEntity>(namingProvider, partition, sp);
        });
    }

    /// <summary>
    /// Orchestrates full repository name resolution.
    /// Composes: [StorageName] or [StorageName][Separator][ConcretePartition]
    /// </summary>
    private static string GetTargetRepositoryName<TEntity>(
        INamingProvider np,
        string? partition,
        IServiceProvider services)
        where TEntity : class
    {
        // Get and trim storage name
        var storageName = np.GetStorageName(typeof(TEntity), services).Trim();

        // Trim and check partition
        var trimmedPartition = partition?.Trim();
        if (string.IsNullOrEmpty(trimmedPartition))
            return storageName;

        // Compose with partition
        var repositorySeparator = np.RepositorySeparator;
        var concretePartition = np.GetConcretePartition(trimmedPartition).Trim();

        return storageName + repositorySeparator + concretePartition;
    }

    private static INamingProvider ResolveProvider(IServiceProvider sp, string providerKey)
    {
        // Factories are registered as IDataAdapterFactory and IVectorAdapterFactory, not INamingProvider
        // We must query for the concrete types then cast to INamingProvider
        var dataFactories = sp.GetServices<IDataAdapterFactory>().Cast<INamingProvider>();

        // Try to get vector factories if available (optional dependency)
        // Use reflection to avoid hard dependency on Koan.Data.Vector.Abstractions
        IEnumerable<INamingProvider> vectorFactories = Enumerable.Empty<INamingProvider>();
        try
        {
            var vectorAdapterFactoryType = Type.GetType("Koan.Data.Vector.Abstractions.IVectorAdapterFactory, Koan.Data.Vector.Abstractions");
            if (vectorAdapterFactoryType != null)
            {
                var getServicesMethod = typeof(ServiceProviderServiceExtensions)
                    .GetMethod(nameof(ServiceProviderServiceExtensions.GetServices))!
                    .MakeGenericMethod(vectorAdapterFactoryType);
                var services = (System.Collections.IEnumerable)getServicesMethod.Invoke(null, new object[] { sp })!;
                vectorFactories = services.Cast<INamingProvider>();
            }
        }
        catch
        {
            // Vector abstractions not available, continue with data factories only
        }

        // Combine both factory types
        var allFactories = dataFactories.Concat(vectorFactories);

        var provider = allFactories.FirstOrDefault(p =>
            string.Equals(p.Provider, providerKey, StringComparison.OrdinalIgnoreCase));

        if (provider == null)
        {
            throw new InvalidOperationException(
                $"No adapter registered for provider '{providerKey}'. " +
                $"Ensure an IDataAdapterFactory or IVectorAdapterFactory implementation is registered for this provider.");
        }

        return provider;
    }
}
