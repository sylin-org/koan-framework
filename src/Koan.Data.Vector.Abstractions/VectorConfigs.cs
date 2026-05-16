using System;
using System.Collections.Concurrent;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Koan.Data.Abstractions;

namespace Koan.Data.Vector.Abstractions;

/// <summary>
/// Configuration resolution for vector entities.
/// Parallel to AggregateConfigs but for vector layer.
/// Caches provider selection per (entity, key) combination.
/// </summary>
public static class VectorConfigs
{
    private static readonly ConcurrentDictionary<(Type, Type), object> Cache = new();

    /// <summary>
    /// Get vector configuration for entity type.
    /// Resolves vector provider via [VectorAdapter] attribute or highest-priority factory.
    /// </summary>
    public static VectorConfig<TEntity, TKey> Get<TEntity, TKey>(IServiceProvider sp)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var key = (typeof(TEntity), typeof(TKey));
        if (Cache.TryGetValue(key, out var existing))
            return (VectorConfig<TEntity, TKey>)existing;

        var provider = ResolveProvider(typeof(TEntity), sp);
        var cfg = new VectorConfig<TEntity, TKey>(provider, sp);
        Cache[key] = cfg;
        return cfg;
    }

    /// <summary>
    /// Clear cached configurations. Used for testing/host reset.
    /// </summary>
    internal static void Reset() => Cache.Clear();

    private static string ResolveProvider(Type entityType, IServiceProvider sp)
    {
        // Check for explicit [VectorAdapter("provider")] attribute
        var attr = (VectorAdapterAttribute?)Attribute.GetCustomAttribute(
            entityType,
            typeof(VectorAdapterAttribute));

        if (attr is not null && !string.IsNullOrWhiteSpace(attr.Provider))
            return attr.Provider;

        // Fallback: Highest-priority vector adapter factory
        return DefaultVectorProvider(sp);
    }

    private static string DefaultVectorProvider(IServiceProvider sp)
    {
        var factories = sp.GetServices<IVectorAdapterFactory>().ToList();

        if (factories.Count == 0)
            throw new InvalidOperationException(
                "No IVectorAdapterFactory registered. Ensure vector connector package is referenced.");

        // Rank by ProviderPriorityAttribute (higher wins), then by name for stability
        var ranked = factories
            .Select(f => new
            {
                Factory = f,
                Priority = (f.GetType().GetCustomAttributes(typeof(ProviderPriorityAttribute), false)
                    .FirstOrDefault() as ProviderPriorityAttribute)?.Priority ?? 0,
                Name = f.GetType().Name
            })
            .OrderByDescending(x => x.Priority)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var chosen = ranked.First().Factory.Provider;

        // TODO: Add ILogger injection for diagnostics
        // Log warning about implicit provider selection

        return chosen;
    }
}
