using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Sora.Secrets.Abstractions;
using Sora.Secrets.Core.Providers;
using Sora.Secrets.Core.Resolver;

namespace Sora.Secrets.Core.DI;

public sealed class SecretsOptions
{
    public TimeSpan DefaultTtl { get; set; } = TimeSpan.FromMinutes(5);
}

public interface ISecretsBuilder
{
    ISecretsBuilder AddProvider<T>() where T : class, ISecretProvider;
}

internal sealed class SecretsBuilder(IServiceCollection services) : ISecretsBuilder
{
    public ISecretsBuilder AddProvider<T>() where T : class, ISecretProvider
    {
        services.AddSingleton<ISecretProvider, T>();
        return this;
    }
}

public static class ServiceCollectionExtensions
{
    public static ISecretsBuilder AddSoraSecrets(this IServiceCollection services, Action<SecretsOptions>? configure = null)
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
