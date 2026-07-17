using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Koan.Core.Composition;
using Koan.Data.Abstractions;
using Koan.Core;
using Koan.Data.Core.Infrastructure;
using Koan.Data.Core.Routing;

namespace Koan.Data.Core.Composition;

/// <summary>
/// Enriches the resolved composition twin (P1.1) with the data pillar's runtime-resolved state:
/// the <c>data:default</c> adapter election (and any configured named sources) plus the entities
/// resolved so far. The active retained Data module invokes this projector; it owns no lifecycle.
/// </summary>
internal static class DataCompositionFacts
{
    public static void Project(KoanCompositionBuilder builder, IServiceProvider services, string source)
    {
        ContributeElections(builder, services, source);
        ContributeEntities(builder, services);
        ContributeLifecycle(builder, services, source);
    }

    // Projects the canonical host-owned default decision. Named configured sources remain
    // separate explicit elections; this contributor never re-ranks providers.
    private static void ContributeElections(KoanCompositionBuilder builder, IServiceProvider services, string source)
    {
        var registry = services.GetService<DataSourceRegistry>();

        if (registry is not null)
        {
            try
            {
                var decision = AdapterResolver.ResolveDefault(services);
                builder.AddElection(
                    decision.Receipt,
                    source,
                    Constants.Diagnostics.Codes.AdapterSelected);
            }
            catch (AdapterResolutionException exception)
            {
                builder.AddRejection(
                    "data:default",
                    exception.ReasonCode,
                    exception.Correction,
                    source,
                    Constants.Diagnostics.Codes.AdapterRejected);
            }
            catch (InvalidOperationException)
            {
                builder.AddRejection(
                    "data:default",
                    Constants.Diagnostics.Reasons.NoFactory,
                    "Reference a Koan data adapter or configure Koan:Data:Sources:Default:Adapter.",
                    source,
                    Constants.Diagnostics.Codes.AdapterRejected);
            }
        }

        if (registry is not null)
        {
            foreach (var name in registry.GetSourceNames())
            {
                if (string.Equals(name, "Default", StringComparison.OrdinalIgnoreCase)) continue;
                var src = registry.GetSource(name);
                if (src is { } s && !string.IsNullOrWhiteSpace(s.Adapter))
                    builder.AddElection($"data:{name.ToLowerInvariant()}", s.Adapter, "configured-source");
            }
        }
    }

    // Best-effort: IDataDiagnostics reflects AggregateConfigs.Cache, populated lazily on first
    // Data<T,K> access — so at boot this is typically the entities the app touched during startup.
    private static void ContributeEntities(KoanCompositionBuilder builder, IServiceProvider services)
    {
        var diagnostics = services.GetService<IDataDiagnostics>();
        if (diagnostics is null) return;

        foreach (var entity in diagnostics.GetEntityConfigsSnapshot())
        {
            var type = entity.EntityType;
            var shortName = ShortTypeName(type);
            builder.AddEntity(shortName);
        }
    }

    private static void ContributeLifecycle(KoanCompositionBuilder builder, IServiceProvider services, string source)
    {
        var diagnostics = services.GetService<IDataDiagnostics>();
        if (diagnostics is null) return;

        foreach (var lifecycle in diagnostics.GetLifecyclePlansSnapshot())
        {
            var type = lifecycle.EntityType;
            var shortName = ShortTypeName(type);
            var phases = lifecycle.HandlerCounts
                .Where(pair => pair.Value != 0)
                .Select(pair => $"{pair.Key}:{pair.Value}")
                .ToArray();
            var subject = $"data:lifecycle:{shortName.ToLowerInvariant()}";
            builder.AddCapability(subject, phases);
            builder.AddObservation(
                Constants.Diagnostics.Codes.LifecycleSelected,
                subject,
                $"Koan composed {lifecycle.TotalHandlers} persistence lifecycle handler(s) for '{shortName}'.",
                "host-composition",
                source);
        }
    }

    private static string ShortTypeName(string type)
    {
        var separator = Math.Max(type.LastIndexOf('.'), type.LastIndexOf('+'));
        return separator < 0 ? type : type[(separator + 1)..];
    }
}
