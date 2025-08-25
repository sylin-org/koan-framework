using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sora.AI.Contracts;
using Sora.AI.Contracts.Options;
using Sora.AI.Contracts.Routing;
using Sora.Core;

namespace Sora.AI;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAi(this IServiceCollection services, IConfiguration? config = null)
    {
        if (config is not null)
        {
            services.AddSoraOptions<AiOptions>(config, "Sora:Ai");
        }
        else
        {
            services.AddSoraOptions<AiOptions>("Sora:Ai");
        }

        services.TryAddSingleton<IAiAdapterRegistry, InMemoryAdapterRegistry>();
        services.TryAddSingleton<IAiRouter, DefaultAiRouter>();
        services.TryAddSingleton<IAi, RouterAi>();
        return services;
    }
}