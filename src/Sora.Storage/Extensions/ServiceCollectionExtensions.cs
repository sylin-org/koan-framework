namespace Microsoft.Extensions.DependencyInjection;

using Microsoft.Extensions.Configuration;
using Sora.Core;
using Sora.Storage;
using Sora.Storage.Options;
using Sora.Storage.Infrastructure;

public static class StorageServiceCollectionExtensions
{
    public static IServiceCollection AddSoraStorage(this IServiceCollection services, IConfiguration config)
    {
        services.AddLogging();
    services.AddSoraOptions<StorageOptions>(config, StorageConstants.Constants.Configuration.Section);
        services.AddSingleton<IStorageService, StorageService>();
        return services;
    }
}
