using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Koan.Data.Abstractions;
using Koan.Data.Vector.Abstractions;

namespace Koan.Data.Vector.Abstractions.Configuration;

/// <summary>
/// Resolves vector collection/index identifiers by routing to the appropriate
/// <see cref="IVectorAdapterFactory.ResolveStorage"/>. The factory owns its own cache.
/// </summary>
public static class VectorAdapterNaming
{
    public static string GetOrCompute<TEntity, TKey>(IServiceProvider sp)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var cfg = VectorConfigs.Get<TEntity, TKey>(sp);
        var factory = sp.GetServices<IVectorAdapterFactory>().FirstOrDefault(f => f.CanHandle(cfg.Provider))
            ?? throw new InvalidOperationException($"No vector adapter factory for provider '{cfg.Provider}'.");
        return factory.ResolveStorage(typeof(TEntity), Koan.Data.Core.EntityContext.Current?.Partition, sp);
    }
}
