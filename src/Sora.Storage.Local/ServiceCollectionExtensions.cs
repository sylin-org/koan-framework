namespace Microsoft.Extensions.DependencyInjection;

using Microsoft.Extensions.Configuration;
using Sora.Storage;
using Sora.Storage.Local;

public static class LocalStorageServiceCollectionExtensions
{
    public static IServiceCollection AddSoraLocalStorageProvider(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<LocalStorageOptions>().Bind(config.GetSection("Sora:Storage:Providers:Local"));
        services.AddSingleton<IStorageProvider, LocalStorageProvider>();
        return services;
    }
}
