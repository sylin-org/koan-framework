using System;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Koan.Core.Composition;
using Koan.Data.Abstractions;

namespace Koan.Data.Core.Composition;

/// <summary>
/// Enriches the resolved composition twin (P1.1) with the data pillar's runtime-resolved state:
/// the <c>data:default</c> adapter election (and any configured named sources) plus the entities
/// resolved so far. Discovered automatically via <see cref="IKoanCompositionContributor"/> —
/// referencing a data adapter is what makes the lockfile describe the data composition.
/// </summary>
internal sealed class DataCompositionContributor : IKoanCompositionContributor
{
    public void Contribute(KoanCompositionBuilder builder, IServiceProvider services)
    {
        ContributeElections(builder, services);
        ContributeEntities(builder, services);
    }

    // Mirrors AdapterResolver's framework-default chain (Priority 4 → 5): a configured "Default"
    // source wins ("default-source"); otherwise the highest-[ProviderPriority] factory does
    // ("reference-priority"). Named configured sources are reported as data:{name}.
    private static void ContributeElections(KoanCompositionBuilder builder, IServiceProvider services)
    {
        var registry = services.GetService<DataSourceRegistry>();

        var defaultSource = registry?.GetSource("Default");
        if (defaultSource is { } ds && !string.IsNullOrWhiteSpace(ds.Adapter))
        {
            builder.AddElection("data:default", ds.Adapter, "default-source");
        }
        else
        {
            var factories = services.GetServices<IDataAdapterFactory>().ToList();
            if (factories.Count > 0)
            {
                var ranked = factories
                    .Select(f => new
                    {
                        Name = f.GetType().Name,
                        Priority = f.GetType().GetCustomAttribute<ProviderPriorityAttribute>()?.Priority ?? 0,
                    })
                    .OrderByDescending(x => x.Priority)
                    .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                    .First();
                var adapter = ranked.Name
                    .Replace("AdapterFactory", "", StringComparison.OrdinalIgnoreCase)
                    .ToLowerInvariant();
                builder.AddElection("data:default", adapter, "reference-priority", ranked.Priority);
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
            var shortName = type.Contains('.', StringComparison.Ordinal)
                ? type[(type.LastIndexOf('.') + 1)..]
                : type;
            builder.AddEntity(shortName);
        }
    }
}
