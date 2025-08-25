namespace Microsoft.Extensions.DependencyInjection;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Sora.Storage;
using Sora.Storage.Options;

public static class StorageServiceCollectionExtensions
{
    public static IServiceCollection AddSoraStorage(this IServiceCollection services, IConfiguration config)
    {
        services.AddLogging();
        services
            .AddOptions<StorageOptions>()
            .Bind(config.GetSection("Sora:Storage"))
            .ValidateDataAnnotations();
        services.AddSingleton<IStorageService, StorageService>();
        return services;
    }
}
