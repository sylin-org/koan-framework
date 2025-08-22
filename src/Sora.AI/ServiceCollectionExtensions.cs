using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sora.AI.Contracts;
using Sora.AI.Contracts.Options;
using Sora.AI.Contracts.Routing;

namespace Sora.AI;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAi(this IServiceCollection services, IConfiguration? config = null)
    {
        services.AddOptions<AiOptions>();
        if (config is not null)
            services.Configure<AiOptions>(config.GetSection("Sora:Ai"));

        services.TryAddSingleton<IAiAdapterRegistry, InMemoryAdapterRegistry>();
        services.TryAddSingleton<IAiRouter, DefaultAiRouter>();
        services.TryAddSingleton<IAi, RouterAi>();
        return services;
    }
}