using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Hosting.Registry;
using Koan.Core.Orchestration.Abstractions;
using Koan.Core.Orchestration.Composition;
using Koan.Core.Semantics;

namespace Koan.Core.Orchestration;

/// <summary>
/// Core-owned service-discovery registration and provenance.
/// </summary>
internal static class ServiceDiscoveryBootstrap
{
    public static void Register(IServiceCollection services)
    {
        var planBuilder = new ServiceDiscoveryPlanBuilder();
        SemanticCompositionSession.GetOrCreate(services).ScheduleContributions<
            DiscoveryContributionTarget,
            ServiceDiscoveryPlan>(
            owner => planBuilder.ForOwner(owner.Value),
            planBuilder.Build,
            static (collection, plan) =>
            {
                collection.Replace(ServiceDescriptor.Singleton(plan));
                foreach (var source in plan.Sources)
                {
                    collection.TryAdd(ServiceDescriptor.Singleton(source.SourceType, source.SourceType));
                }
            });

        // Baseline/manual hosts have a complete empty plan. Semantic composition replaces this with a
        // compiled host plan when optional discovery layers are active.
        services.TryAddSingleton(ServiceDiscoveryPlan.Empty);
        services.TryAddSingleton(provider => new ServiceDiscoveryRuntime(
            provider,
            provider.GetRequiredService<ServiceDiscoveryPlan>()));

        // Register the service discovery coordinator as singleton
        services.TryAddSingleton<IServiceDiscoveryCoordinator, ServiceDiscoveryCoordinator>();

        // Register OrchestrationAwareServiceDiscovery to delegate to coordinator
        services.TryAddScoped<IOrchestrationAwareServiceDiscovery>(provider =>
            new OrchestrationAwareServiceDiscovery(
                provider.GetRequiredService<IServiceDiscoveryCoordinator>(),
                provider.GetRequiredService<IConfiguration>(),
                provider.GetRequiredService<ILogger<OrchestrationAwareServiceDiscovery>>()));

        // Auto-register all IServiceDiscoveryAdapter implementations discovered at compile time
        var adapters = KoanRegistry.GetServiceDiscoveryAdapters();

        foreach (var adapter in adapters)
        {
            services.TryAddSingleton(typeof(IServiceDiscoveryAdapter), adapter.ServiceType);
        }
    }

    public static void Report(Koan.Core.Provenance.ProvenanceModuleWriter module)
    {
        var adapters = KoanRegistry.GetServiceDiscoveryAdapters();

        module.AddNote($"ServiceDiscoveryAdapters: {adapters.Length}");

        foreach (var adapter in adapters)
        {
            module.AddNote($"  • {adapter.ServiceType.Name}");
        }
    }
}
