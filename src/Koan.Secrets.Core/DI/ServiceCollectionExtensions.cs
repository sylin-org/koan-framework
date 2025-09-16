using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Koan.Secrets.Abstractions;
using Koan.Secrets.Core.Providers;
using Koan.Secrets.Core.Resolver;

namespace Koan.Secrets.Core.DI;

public static class ServiceCollectionExtensions
{
    public static ISecretsBuilder AddKoanSecrets(this IServiceCollection services, Action<SecretsOptions>? configure = null)
    {
        services.AddOptions<SecretsOptions>();
        if (configure is not null) services.Configure(configure);
        services.AddSingleton<IMemoryCache, MemoryCache>();
        services.AddSingleton<ISecretProvider, EnvSecretProvider>();
        services.AddSingleton<ISecretProvider, ConfigurationSecretProvider>();
        services.AddSingleton<ISecretResolver, ChainSecretResolver>();
        return new SecretsBuilder(services);
    }
}
