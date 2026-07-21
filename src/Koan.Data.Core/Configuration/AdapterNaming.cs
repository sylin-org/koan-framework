using Koan.Data.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Core.Configuration;

/// <summary>
/// Resolves storage identity through the same host-owned typed provider decision used for repository
/// construction. The provider's naming implementation owns its own final-name memoization.
/// </summary>
public static class AdapterNaming
{
    public static string GetOrCompute<TEntity, TKey>(IServiceProvider services)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var sources = services.GetRequiredService<DataSourceRegistry>();
        var decision = AdapterResolver.ResolveDecisionForEntity<TEntity>(services, sources);
        return decision.Factory.ResolveStorage(
            typeof(TEntity),
            EntityContext.Current?.Partition,
            services);
    }
}
