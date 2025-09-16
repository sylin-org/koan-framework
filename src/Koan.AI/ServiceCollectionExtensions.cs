using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Koan.AI.Contracts;
using Koan.AI.Contracts.Options;
using Koan.AI.Contracts.Routing;
using Koan.Core;
using Koan.Core.Modules;

namespace Koan.AI;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAi(this IServiceCollection services, IConfiguration? config = null)
    {
        if (config is not null)
        {
            services.AddKoanOptions<AiOptions>(config, "Koan:Ai");
        }
        else
        {
            services.AddKoanOptions<AiOptions>("Koan:Ai");
        }

        services.TryAddSingleton<IAiAdapterRegistry, InMemoryAdapterRegistry>();
        services.TryAddSingleton<IAiRouter, DefaultAiRouter>();
        services.TryAddSingleton<IAi, RouterAi>();
        return services;
    }
}