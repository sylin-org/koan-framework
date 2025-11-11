using Microsoft.Extensions.DependencyInjection;
using Koan.Data.Abstractions;
using System.Collections.Concurrent;

namespace Koan.Data.Core;

public static class AggregateConfigs
{
    private static readonly ConcurrentDictionary<(Type, Type), object> Cache = new();

    public static AggregateConfig<TEntity, TKey> Get<TEntity, TKey>(IServiceProvider sp)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var key = (typeof(TEntity), typeof(TKey));
        if (Cache.TryGetValue(key, out var existing)) return (AggregateConfig<TEntity, TKey>)existing;

        var provider = ResolveProvider(typeof(TEntity)) ?? DefaultProvider(sp);
        var idSpec = AggregateMetadata.GetIdSpec(typeof(TEntity));
        var cfg = new AggregateConfig<TEntity, TKey>(provider, idSpec, sp);
        Cache[key] = cfg;
        return cfg;
    }

    // Testing/host reset: clear cached configs to avoid cross-root contamination
    internal static void Reset() => Cache.Clear();

    private static string? ResolveProvider(Type aggregateType)
    {
        // Prefer explicit SourceAdapter if present, then fall back to legacy DataAdapter
        var src = (SourceAdapterAttribute?)Attribute.GetCustomAttribute(aggregateType, typeof(SourceAdapterAttribute));
        if (src is not null && !string.IsNullOrWhiteSpace(src.Provider)) return src.Provider;
        var data = (DataAdapterAttribute?)Attribute.GetCustomAttribute(aggregateType, typeof(DataAdapterAttribute));
        return data?.Provider;
    }

    private static string DefaultProvider(IServiceProvider sp)
    {
        var factories = sp.GetServices<IDataAdapterFactory>().ToList();
    if (factories.Count == 0) throw new InvalidOperationException("No IDataAdapterFactory instances registered. Ensure services.AddKoanDataCore() has been called and a data adapter module is referenced.");

        // Rank by ProviderPriorityAttribute (higher wins), then by type name for stability
        var ranked = factories
            .Select(f => new
            {
                Factory = f,
                Priority = (f.GetType().GetCustomAttributes(typeof(ProviderPriorityAttribute), inherit: false).FirstOrDefault() as ProviderPriorityAttribute)?.Priority ?? 0,
                Name = f.GetType().Name
            })
            .OrderByDescending(x => x.Priority)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var chosen = ranked.First().Factory.GetType().Name;
        const string suffix = "AdapterFactory";
        if (chosen.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) chosen = chosen[..^suffix.Length];
        return chosen.ToLowerInvariant();
    }
}

// Public-facing shim to access per-entity bags without exposing internal types broadly