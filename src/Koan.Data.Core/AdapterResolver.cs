using System.Reflection;
using Koan.Data.Abstractions;
using Koan.Data.Core.Infrastructure;
using Koan.Data.Core.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Core;

/// <summary>
/// Owns Data's typed source/provider precedence. Exact user intent is resolved through the host catalog
/// and never weakens to an unrelated provider.
/// </summary>
internal static class AdapterResolver
{
    public static (string Adapter, string Source) ResolveForEntity<TEntity>(
        IServiceProvider services,
        DataSourceRegistry sourceRegistry)
        where TEntity : class =>
        ResolveDecisionForEntity<TEntity>(services, sourceRegistry).ToTuple();

    internal static AdapterResolutionDecision ResolveDecisionForEntity<TEntity>(
        IServiceProvider services,
        DataSourceRegistry sourceRegistry)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(sourceRegistry);

        var providers = services.GetRequiredService<DataProviderCatalog>();
        var context = EntityContext.Current;
        var routed = RoutedSource.Resolve(typeof(TEntity), context);

        if (routed.Kind == RouteKind.Explicit)
        {
            var source = sourceRegistry.GetSource(routed.Source!);
            if (source is null)
            {
                throw new InvalidOperationException(
                    $"Source '{routed.Source}' is not configured. Check Koan:Data:Sources:{routed.Source} configuration.");
            }

            if (string.IsNullOrWhiteSpace(source.Adapter))
            {
                throw new InvalidOperationException(
                    $"Source '{routed.Source}' does not specify an adapter. " +
                    $"Add 'Adapter' to Koan:Data:Sources:{routed.Source}.");
            }

            return Required(
                providers,
                source.Adapter,
                routed.Source!,
                Constants.Diagnostics.Reasons.ContextSource,
                $"Correct Koan:Data:Sources:{routed.Source}:Adapter or reference the requested connector.");
        }

        if (routed.Kind == RouteKind.DatabaseAxis)
        {
            var routedKey = routed.Source!;
            var source = sourceRegistry.GetSource(routedKey);
            if (source is null)
            {
                throw new InvalidOperationException(
                    $"Database-mode axis routed entity '{typeof(TEntity).Name}' to data source '{routedKey}', which is " +
                    $"not configured (provisioning posture: {nameof(ProvisioningPosture.ExternalOnly)}). " +
                    $"Add Koan:Data:Sources:{routedKey}:{{Adapter,ConnectionString}}, or pre-provision the source. (ARCH-0102 §3)");
            }

            if (string.IsNullOrWhiteSpace(source.Adapter))
            {
                throw new InvalidOperationException(
                    $"Database-mode axis routed entity '{typeof(TEntity).Name}' to data source '{routedKey}', which does " +
                    $"not specify an adapter. Add 'Adapter' to Koan:Data:Sources:{routedKey}.");
            }

            return Required(
                providers,
                source.Adapter,
                routedKey,
                Constants.Diagnostics.Reasons.DatabaseAxis,
                $"Correct Koan:Data:Sources:{routedKey}:Adapter or reference the requested connector.");
        }

        if (!string.IsNullOrWhiteSpace(context?.Adapter))
        {
            return Required(
                providers,
                context.Adapter,
                "Default",
                Constants.Diagnostics.Reasons.ContextAdapter,
                "Correct EntityContext.Adapter or reference the requested connector.");
        }

        var entityAdapter = ResolveFromAttribute<TEntity>();
        if (entityAdapter is not null)
        {
            return Required(
                providers,
                entityAdapter,
                "Default",
                Constants.Diagnostics.Reasons.EntityAttribute,
                $"Correct the provider decoration on '{typeof(TEntity).Name}' or reference the requested connector.");
        }

        return ResolveDefault(services);
    }

    internal static AdapterResolutionDecision ResolveDefault(IServiceProvider services)
        => services.GetRequiredService<DataDefaultProviderPlan>().Decision;

    private static AdapterResolutionDecision Required(
        DataProviderCatalog providers,
        string requested,
        string source,
        string via,
        string correction)
    {
        var selected = providers.Require(
            requested,
            string.Equals(source, "Default", StringComparison.OrdinalIgnoreCase)
                ? "data:default"
                : "data:source",
            via,
            correction);
        return new AdapterResolutionDecision(selected.Factory, source, selected.Receipt);
    }

    private static string? ResolveFromAttribute<TEntity>() where TEntity : class
    {
        var type = typeof(TEntity);
        var source = type.GetCustomAttribute<SourceAdapterAttribute>();
        if (source is not null && !string.IsNullOrWhiteSpace(source.Provider)) return source.Provider;

        var data = type.GetCustomAttribute<DataAdapterAttribute>();
        return data is not null && !string.IsNullOrWhiteSpace(data.Provider) ? data.Provider : null;
    }
}
