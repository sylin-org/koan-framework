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

        // Register source registry (ADR-0015: No group registry - sources contain members)
        services.TryAddSingleton<IAiSourceRegistry>(sp =>
        {
            var registry = new AiSourceRegistry();
            var configuration = sp.GetRequiredService<IConfiguration>();
            var logger = sp.GetService<ILogger<AiSourceRegistry>>();
            registry.DiscoverFromConfiguration(configuration, logger);
            return registry;
        });

        // TODO: Implement health monitoring (ADR-0015 Phase 5)
        // - Member-level circuit breakers
        // - Source health aggregation
        // - Background health monitor service

        // Register existing infrastructure
        services.TryAddSingleton<IAiAdapterRegistry, InMemoryAdapterRegistry>();
        services.TryAddSingleton<IAiRouter, DefaultAiRouter>();
        services.TryAddSingleton<IAi, RouterAi>();

        return services;
    }
}
