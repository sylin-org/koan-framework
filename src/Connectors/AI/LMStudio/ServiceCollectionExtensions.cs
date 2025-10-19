using Microsoft.Extensions.DependencyInjection;
using Koan.AI.Connector.LMStudio.Options;

namespace Koan.AI.Connector.LMStudio;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLMStudioFromConfig(this IServiceCollection services)
    {
        services.AddOptions<LMStudioOptions[]>();
        return services;
    }
}

