using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.AI.Contracts;
using Koan.AI.Contracts.Options;
using Koan.AI.Contracts.Routing;
using Koan.AI.Contracts.Sources;
using Koan.AI.Health;
using Koan.AI.Infrastructure;
using Koan.AI.Pipeline;
using Koan.AI.Sources;
using Koan.Core;
using Koan.Core.AI;
using Koan.Core.Modules;

namespace Koan.AI;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAi(this IServiceCollection services, IConfiguration? config = null)
    {
        // Register options
        if (config is not null)
        {
            services.AddKoanOptions<AiOptions>(config, ConfigurationConstants.Section);
        }
        else
        {
            services.AddKoanOptions<AiOptions>(ConfigurationConstants.Section);
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

        // ADR-0015 Phase 5: Background health monitoring
        services.AddHttpClient("KoanAiHealthProbe");
        services.AddHostedService<AiSourceHealthMonitor>();
        services.TryAddSingleton<IHealthContributor, AiSourcesHealthContributor>();

        // Register infrastructure
        services.TryAddSingleton<IAiAdapterRegistry, InMemoryAdapterRegistry>();
        services.TryAddSingleton<IAiRecipeProvider>(sp =>
        {
            var configuration = sp.GetRequiredService<IConfiguration>();
            var logger = sp.GetService<ILogger<AiRecipeProvider>>();
            return new AiRecipeProvider(configuration, logger);
        });
        services.TryAddSingleton(sp => new AiCategoryRouter(
            sp.GetRequiredService<IAiAdapterRegistry>(),
            sp.GetRequiredService<IAiSourceRegistry>(),
            sp.GetRequiredService<IOptions<AiOptions>>(),
            sp.GetService<IAiRecipeProvider>(),
            sp.GetService<IAiModelAdvisor>(),
            sp.GetService<ILogger<AiCategoryRouter>>()));

        services.TryAddSingleton<IChatClient, AdapterBackedChatClient>();
        services.TryAddSingleton<IEmbeddingGenerator<string, Embedding<float>>, AdapterBackedEmbeddingGenerator>();
        services.TryAddSingleton<IAiPipeline, KoanAiPipeline>();

        return services;
    }
}
