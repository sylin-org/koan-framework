using System;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Koan.Data.Abstractions;
using Koan.Data.Core.Routing;

namespace Koan.Data.Core;

/// <summary>
/// Resolves adapter and source for an entity according to priority rules.
///
/// Priority chain (first match wins):
/// 1. EntityContext.Current.Source → use source's configured adapter
/// 1.5. Database-mode [DataAxis] route (ARCH-0102 §3) → source derived from the ambient (auto-routing), gated by
///      DatabaseRouteRegistry.IsEmpty so off = byte-identical
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

        // Priority 1 + 1.5 (one decision, ARCH-0103 §4.1): the shared RoutedSource resolves an explicit
        // EntityContext.Source (always wins) else a Database-mode [DataAxis] route — the SAME primitive VectorService
        // routes through, so a Database-mode axis routes the record and vector planes to one source (no split-brain).
        // ctx is reused (read once) so the off path stays a single ambient read (FC-5: off = byte-identical).
        var routed = RoutedSource.Resolve(typeof(TEntity), ctx);

        // Priority 1: Source specified in context → use source's adapter
        if (routed.Kind == RouteKind.Explicit)
        {
            var sourceDefinition = sourceRegistry.GetSource(routed.Source!);
            if (sourceDefinition == null)
                throw new InvalidOperationException(
                    $"Source '{routed.Source}' is not configured. " +
                    $"Check Koan:Data:Sources:{routed.Source} configuration.");

            if (string.IsNullOrWhiteSpace(sourceDefinition.Adapter))
                throw new InvalidOperationException(
                    $"Source '{routed.Source}' does not specify an adapter. " +
                    $"Add 'Adapter' key to Koan:Data:Sources:{routed.Source} configuration.");

            return (sourceDefinition.Adapter, routed.Source!);
        }

        // Priority 1.5 (ARCH-0102 §3 — Database-mode auto-routing): a Database-mode axis derives the data source from
        // the ambient (e.g. tenant → that tenant's DB) with no explicit EntityContext.Source.
        if (routed.Kind == RouteKind.DatabaseAxis)
        {
            var routedKey = routed.Source!;
            var routedDefinition = sourceRegistry.GetSource(routedKey);

            // Provisioning is a posture (ARCH-0102 §3), not a mechanism: external-only (the realized Phase-2 default)
            // fails closed on an absent keyspace rather than silently mis-routing — self-explaining (FC-7). Lazy
            // derivation (Tier-0 zero-config) and eager pre-provisioning are the P6 broker; until then, configure it.
            if (routedDefinition == null)
                throw new InvalidOperationException(
                    $"Database-mode axis routed entity '{typeof(TEntity).Name}' to data source '{routedKey}', which is " +
                    $"not configured (provisioning posture: {nameof(ProvisioningPosture.ExternalOnly)}). " +
                    $"Add Koan:Data:Sources:{routedKey}:{{Adapter,ConnectionString}}, or pre-provision the source. (ARCH-0102 §3)");

            if (string.IsNullOrWhiteSpace(routedDefinition.Adapter))
                throw new InvalidOperationException(
                    $"Database-mode axis routed entity '{typeof(TEntity).Name}' to data source '{routedKey}', which does " +
                    $"not specify an adapter. Add 'Adapter' to Koan:Data:Sources:{routedKey}.");

            return (routedDefinition.Adapter, routedKey);
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

        // Priority 5: Highest priority adapter factory (the shared [ProviderPriority]+CanHandle ranking, ARCH-0103 §4.1).
        var factories = sp.GetServices<IDataAdapterFactory>().ToList();
        if (factories.Count == 0)
            throw new InvalidOperationException(
                "No IDataAdapterFactory instances registered. " +
                "Ensure adapter packages are referenced and AddKoan() has been called.");

        // No desired provider here → highest priority overall; non-null because the list is non-empty (checked above).
        var chosen = FactoryResolver.Resolve(factories, desired: null)!;
        return (FactoryResolver.ProviderName(chosen), "Default");
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
