using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Koan.Data.Abstractions;

namespace Koan.Data.Core.Configuration;

/// <summary>
/// Resolves the storage identifier for an entity by routing to the appropriate adapter factory's
/// <see cref="Abstractions.Naming.INamingProvider.ResolveStorage"/>.
///
/// The factory owns its own <em>name</em> cache (<see cref="Abstractions.Naming.StorageNameGenerator"/>); this
/// helper memoizes the per-provider factory <em>lookup</em> — the DI service enumeration that previously ran on
/// every property access (a repository's <c>TableName</c>/<c>CollectionName</c> getter calls this per op). The
/// lookup is keyed by the <see cref="IServiceProvider"/> via a <see cref="ConditionalWeakTable{TKey,TValue}"/>,
/// so entries release with the provider (no cross-provider leak, no stale factory). Adapter resolution stays
/// ambient-aware (Source/Adapter overrides) and the final name stays partition-aware.
/// </summary>
public static class AdapterNaming
{
    private static readonly ConditionalWeakTable<IServiceProvider, ConcurrentDictionary<string, IDataAdapterFactory>> FactoryByProvider = new();

    public static string GetOrCompute<TEntity, TKey>(IServiceProvider sp)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var sourceRegistry = sp.GetRequiredService<DataSourceRegistry>();
        var (adapter, _) = AdapterResolver.ResolveForEntity<TEntity>(sp, sourceRegistry);
        var factory = ResolveFactory(sp, adapter);
        return factory.ResolveStorage(typeof(TEntity), EntityContext.Current?.Partition, sp);
    }

    private static IDataAdapterFactory ResolveFactory(IServiceProvider sp, string adapter)
    {
        var perProvider = FactoryByProvider.GetValue(
            sp, static _ => new ConcurrentDictionary<string, IDataAdapterFactory>(StringComparer.Ordinal));

        return perProvider.GetOrAdd(adapter, static (a, services) =>
            services.GetServices<IDataAdapterFactory>().FirstOrDefault(f => f.CanHandle(a))
                ?? throw new InvalidOperationException($"No data adapter factory for provider '{a}'."), sp);
    }
}
