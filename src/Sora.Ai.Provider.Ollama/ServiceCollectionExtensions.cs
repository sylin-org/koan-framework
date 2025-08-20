using Microsoft.Extensions.DependencyInjection;
using Sora.Ai.Provider.Ollama.Options;

namespace Sora.Ai.Provider.Ollama;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOllamaFromConfig(this IServiceCollection services)
    {
        services.AddOptions<OllamaServiceOptions[]>();
        // The actual registration work is done by Initializers discovered via SoraInitialization
        return services;
    }
}
