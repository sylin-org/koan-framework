using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Koan.Data.Abstractions;

namespace Koan.Data.Core.Configuration;

/// <summary>
/// Resolves the storage identifier for an entity by routing to the appropriate
/// adapter factory's <see cref="Abstractions.Naming.INamingProvider.ResolveStorage"/>.
///
/// The factory owns its own cache — this is just a lookup helper that finds the
/// right factory based on entity adapter routing.
/// </summary>
public static class AdapterNaming
{
    public static string GetOrCompute<TEntity, TKey>(IServiceProvider sp)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var sourceRegistry = sp.GetRequiredService<DataSourceRegistry>();
        var (adapter, _) = AdapterResolver.ResolveForEntity<TEntity>(sp, sourceRegistry);
        var factory = sp.GetServices<IDataAdapterFactory>().FirstOrDefault(f => f.CanHandle(adapter))
            ?? throw new InvalidOperationException($"No data adapter factory for provider '{adapter}'.");
        return factory.ResolveStorage(typeof(TEntity), EntityContext.Current?.Partition, sp);
    }
}
