using Microsoft.Extensions.DependencyInjection;
using Koan.AI.Connector.Ollama.Options;

namespace Koan.AI.Connector.Ollama;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOllamaFromConfig(this IServiceCollection services)
    {
        services.AddOptions<OllamaServiceOptions[]>();
        // The actual registration work is done by registrars discovered via AppBootstrapper (greenfield)
        return services;
    }
}

