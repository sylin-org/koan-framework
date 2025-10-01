using System;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Koan.Data.Abstractions;

namespace Koan.Data.Core;

/// <summary>
/// Resolves adapter and source for an entity according to priority rules.
///
/// Priority chain (first match wins):
/// 1. EntityContext.Current.Source → use source's configured adapter
/// 2. EntityContext.Current.Adapter → explicit adapter override
/// 3. [DataAdapter] or [SourceAdapter] attribute → entity-level configuration
/// 4. "Default" source (if configured) → framework-level default
/// 5. [ProviderPriority] ranking → fallback to highest priority factory
/// </summary>
internal static class AdapterResolver
{
    /// <summary>
    /// Resolve adapter and source for an entity.
    /// </summary>
    /// <param name="sp">Service provider for factory resolution</param>
    /// <param name="sourceRegistry">Source registry for source lookup</param>
    /// <returns>Tuple of (Adapter, Source)</returns>
    /// <exception cref="InvalidOperationException">Thrown when resolution fails</exception>
    public static (string Adapter, string Source) ResolveForEntity<TEntity>(
        IServiceProvider sp,
        DataSourceRegistry sourceRegistry)
        where TEntity : class
    {
        var ctx = EntityContext.Current;

        // Priority 1: Source specified in context → use source's adapter
        if (!string.IsNullOrWhiteSpace(ctx?.Source))
        {
            var sourceDefinition = sourceRegistry.GetSource(ctx.Source);
            if (sourceDefinition == null)
                throw new InvalidOperationException(
                    $"Source '{ctx.Source}' is not configured. " +
                    $"Check Koan:Data:Sources:{ctx.Source} configuration.");

            if (string.IsNullOrWhiteSpace(sourceDefinition.Adapter))
                throw new InvalidOperationException(
                    $"Source '{ctx.Source}' does not specify an adapter. " +
                    $"Add 'Adapter' key to Koan:Data:Sources:{ctx.Source} configuration.");

            return (sourceDefinition.Adapter, ctx.Source);
        }

        // Priority 2: Adapter specified in context → use with implicit "Default" source
        if (!string.IsNullOrWhiteSpace(ctx?.Adapter))
        {
            return (ctx.Adapter, "Default");
        }

        // Priority 3: Entity [SourceAdapter] or [DataAdapter] attribute
        var entityAdapter = ResolveFromAttribute<TEntity>();
        if (entityAdapter != null)
        {
            // Attribute specified → MUST honor or fail
            return (entityAdapter, "Default");
        }

        // Priority 4: "Default" source configuration
        var defaultSource = sourceRegistry.GetSource("Default");
        if (defaultSource != null && !string.IsNullOrWhiteSpace(defaultSource.Adapter))
        {
            return (defaultSource.Adapter, "Default");
        }

        // Priority 5: Highest priority adapter factory
        var factories = sp.GetServices<IDataAdapterFactory>().ToList();
        if (factories.Count == 0)
            throw new InvalidOperationException(
                "No IDataAdapterFactory instances registered. " +
                "Ensure adapter packages are referenced and AddKoan() has been called.");

        var ranked = factories
            .Select(f => new
            {
                Factory = f,
                Priority = f.GetType()
                    .GetCustomAttribute<ProviderPriorityAttribute>()?.Priority ?? 0,
                Name = f.GetType().Name
            })
            .OrderByDescending(x => x.Priority)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .First();

        // Extract adapter name from factory class name (e.g., "SqliteAdapterFactory" → "sqlite")
        var adapterName = ranked.Name
            .Replace("AdapterFactory", "", StringComparison.OrdinalIgnoreCase)
            .ToLowerInvariant();

        return (adapterName, "Default");
    }

    /// <summary>
    /// Resolve adapter from entity attributes.
    /// Prefers [SourceAdapter] over legacy [DataAdapter].
    /// </summary>
    private static string? ResolveFromAttribute<TEntity>() where TEntity : class
    {
        var type = typeof(TEntity);

        // Prefer SourceAdapter over legacy DataAdapter
        var srcAttr = type.GetCustomAttribute<SourceAdapterAttribute>();
        if (srcAttr != null && !string.IsNullOrWhiteSpace(srcAttr.Provider))
            return srcAttr.Provider;

        var dataAttr = type.GetCustomAttribute<DataAdapterAttribute>();
        if (dataAttr != null && !string.IsNullOrWhiteSpace(dataAttr.Provider))
            return dataAttr.Provider;

        return null;
    }
}
