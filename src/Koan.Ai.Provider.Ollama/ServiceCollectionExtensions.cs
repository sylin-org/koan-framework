using Microsoft.Extensions.DependencyInjection;
using Koan.Ai.Provider.Ollama.Options;

namespace Koan.Ai.Provider.Ollama;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOllamaFromConfig(this IServiceCollection services)
    {
        services.AddOptions<OllamaServiceOptions[]>();
        // The actual registration work is done by registrars discovered via AppBootstrapper (greenfield)
        return services;
    }
}
