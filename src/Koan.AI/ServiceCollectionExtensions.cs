using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Koan.AI.Contracts;
using Koan.AI.Contracts.Options;
using Koan.AI.Contracts.Routing;
using Koan.AI.Contracts.Sources;
using Koan.AI.Sources;
using Koan.Core;
using Koan.Core.Modules;

namespace Koan.AI;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAi(this IServiceCollection services, IConfiguration? config = null)
    {
        // Register options
        if (config is not null)
        {
            services.AddKoanOptions<AiOptions>(config, "Koan:Ai");
        }
        else
        {
            services.AddKoanOptions<AiOptions>("Koan:Ai");
        }

        // Register source and group registries (NEW - ADR-0014)
        services.TryAddSingleton<IAiSourceRegistry>(sp =>
        {
            var registry = new AiSourceRegistry();
            var configuration = sp.GetRequiredService<IConfiguration>();
            var logger = sp.GetService<ILogger<AiSourceRegistry>>();
            registry.DiscoverFromConfiguration(configuration, logger);
            return registry;
        });

        services.TryAddSingleton<IAiGroupRegistry>(sp =>
        {
            var registry = new AiGroupRegistry();
            var configuration = sp.GetRequiredService<IConfiguration>();
            var logger = sp.GetService<ILogger<AiGroupRegistry>>();
            registry.DiscoverFromConfiguration(configuration, logger);
            return registry;
        });

        // Register health monitoring infrastructure (ADR-0014 Phase 4)
        services.TryAddSingleton<ISourceHealthRegistry>(sp =>
        {
            var healthRegistry = new SourceHealthRegistry(sp.GetService<ILogger<SourceHealthRegistry>>());

            // Register circuit breaker configs for all groups
            var groupRegistry = sp.GetRequiredService<IAiGroupRegistry>();
            var sourceRegistry = sp.GetRequiredService<IAiSourceRegistry>();

            foreach (var group in groupRegistry.GetAllGroups())
            {
                var sources = sourceRegistry.GetSourcesInGroup(group.Name);
                foreach (var source in sources)
                {
                    healthRegistry.RegisterSource(source.Name, group.CircuitBreaker);
                }
            }

            return healthRegistry;
        });

        // Register health monitor background service
        services.AddHostedService<AiSourceHealthMonitor>();

        // Register existing infrastructure
        services.TryAddSingleton<IAiAdapterRegistry, InMemoryAdapterRegistry>();
        services.TryAddSingleton<IAiRouter, DefaultAiRouter>();
        services.TryAddSingleton<IAi, RouterAi>();

        return services;
    }
}
