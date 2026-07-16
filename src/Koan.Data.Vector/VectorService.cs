using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Koan.Data.Abstractions;
using Koan.Core;
using Koan.Data.Core.Routing;
using Koan.Data.Vector.Abstractions;

namespace Koan.Data.Vector;

internal sealed class VectorService(IServiceProvider sp) : IVectorService
{
    // Cached per (entity, key, source): a Database-mode axis routes distinct ambients to distinct physical stores,
    // so each routed source gets its own repository instance (the same per-source cache discipline the record facade
    // uses). Off-axis, every op resolves to "Default" and shares one entry — byte-identical to the prior (entity,key) cache.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(Type, Type, string), object> _cache = new();

    public IVectorSearchRepository<TEntity, TKey>? TryGetRepository<TEntity, TKey>() where TEntity : class, IEntity<TKey> where TKey : notnull
    {
        // ARCH-0103 §4.1 — route through the SAME decision the record plane makes (explicit EntityContext.Source >
        // Database-mode axis route > Default). This is the split-brain fix: a Database-mode tenant's embedding now lands
        // in the tenant's store, not the shared one.
        var source = RoutedSource.Resolve<TEntity>().Source ?? "Default";

        var key = (typeof(TEntity), typeof(TKey), source);
        if (_cache.TryGetValue(key, out var existing)) return (IVectorSearchRepository<TEntity, TKey>?)existing;
        var factories = sp.GetServices<IVectorAdapterFactory>().ToList();
        if (factories.Count == 0) return null;
        // 1) Entity-level vector role
        string? desired = (Attribute.GetCustomAttribute(typeof(TEntity), typeof(VectorAdapterAttribute))
            as VectorAdapterAttribute)?.Provider;
        // 2) App defaults
        desired ??= sp.GetService<IOptions<VectorDefaultsOptions>>()?.Value?.DefaultProvider;
        // 3) Entity source provider (role-based)
        if (string.IsNullOrWhiteSpace(desired))
        {
            var src = (SourceAdapterAttribute?)Attribute.GetCustomAttribute(typeof(TEntity), typeof(SourceAdapterAttribute));
            if (src is not null && !string.IsNullOrWhiteSpace(src.Provider)) desired = src.Provider;
            else
            {
                var data = (DataAdapterAttribute?)Attribute.GetCustomAttribute(typeof(TEntity), typeof(DataAdapterAttribute));
                if (data is not null && !string.IsNullOrWhiteSpace(data.Provider)) desired = data.Provider;
            }
        }
        // 4) Default to the highest-priority DATA provider name when none specified (shared FactoryResolver ranking).
        if (string.IsNullOrWhiteSpace(desired))
        {
            var top = FactoryResolver.Resolve(sp.GetServices<IDataAdapterFactory>().ToList(), desired: null);
            if (top is not null) desired = FactoryResolver.ProviderName(top);
        }
        // Resolve the vector factory by the shared [ProviderPriority]+CanHandle ranking (record + vector use one rule).
        var factory = FactoryResolver.Resolve(factories, desired);
        // Pass the routed source so the adapter realizes per-source physical placement (InMemoryVector/SqliteVec in P1).
        var inner = factory?.Create<TEntity, TKey>(sp, source);
        // GAP C 0.3: wrap with the data-axis isolation decorator (write-stamp the registered equality axes into the
        // vector metadata + AND a scope read-filter into every search). Off (no managed field) ⇒ pass-through.
        var repo = inner is not null ? new ScopedVectorRepository<TEntity, TKey>(inner, sp) : null;
        if (repo is not null) _cache[key] = repo;
        return repo;
    }
}
