using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Koan.Core.Composition;
using Koan.Data.Abstractions;
using Koan.Core;
using Koan.Data.Core.Infrastructure;
using Koan.Data.Core.Routing;
using Koan.Core.Hosting.Registry;

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
        ContributeEntities(builder);
        ContributeSourcePlans(builder, services, source);
        ContributeDefaultRoute(builder, services, source);
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

    private static void ContributeEntities(KoanCompositionBuilder builder)
    {
        foreach (var type in KoanRegistry.GetDiscoveredImplementors(typeof(IEntity))
                     .Where(static type => type.IsClass && !type.IsAbstract && !type.ContainsGenericParameters)
                     .OrderBy(static type => type.FullName ?? type.Name, StringComparer.Ordinal))
            builder.AddEntity(ShortTypeName(type.FullName ?? type.Name));
    }

    private static void ContributeDefaultRoute(
        KoanCompositionBuilder builder,
        IServiceProvider services,
        string source)
    {
        var authority = services.GetService<DefaultDataRouteAuthority>();
        if (authority is null) return;

        var current = authority.Current;
        var subject = "data:route:default";
        var tokens = new List<string>
        {
            $"adapter:{current.Plan.Adapter}",
            $"source:{current.Plan.Source}",
            $"authority-revision:{current.AuthorityRevision}",
            $"content-generation:{current.ContentGeneration}"
        };
        tokens.AddRange(current.QuarantinedRouteIdentities
            .Order(StringComparer.Ordinal)
            .Select(static route => $"quarantined:{route}"));
        builder.AddCapability(subject, tokens);
        builder.AddObservation(
            Constants.Diagnostics.Codes.DefaultRouteSelected,
            subject,
            $"Koan's active default Data route is source '{current.Plan.Source}' through provider " +
            $"'{current.Plan.Adapter}' at authority revision {current.AuthorityRevision} and content generation " +
            $"{current.ContentGeneration}.",
            "durable-route-authority",
            source);
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

    private static void ContributeSourcePlans(KoanCompositionBuilder builder, IServiceProvider services, string source)
    {
        var diagnostics = services.GetService<IDataDiagnostics>();
        if (diagnostics is null) return;

        foreach (var plan in diagnostics.GetSourcePlansSnapshot())
        {
            var subject = $"data:source:{plan.RouteIdentity}";
            builder.AddCapability(subject, plan.ClaimReferences);
            builder.AddObservation(
                Constants.Diagnostics.Codes.SourcePlanSelected,
                subject,
                $"Koan selected provider '{plan.Adapter}' with StorageLifecycle={plan.StorageLifecycle}, " +
                $"Access={plan.Access}, ReadLanes={plan.ReadLanes.Count}.",
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
