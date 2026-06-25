using Microsoft.Extensions.DependencyInjection;
using Koan.Core.Modules;
using Koan.Storage.Abstractions;

namespace Koan.Storage.Extensions;

using Microsoft.Extensions.Configuration;
using Koan.Core;
using Koan.Storage;
using Koan.Storage.Infrastructure;
using Koan.Storage.Options;

public static class StorageServiceCollectionExtensions
{
    public static IServiceCollection AddKoanStorage(this IServiceCollection services, IConfiguration config)
    {
        services.AddLogging();
        services.AddKoanOptions<StorageOptions>(config, StorageConstants.Constants.Configuration.Section);
        // STOR-0011: IStorageService is the data-axis isolation chokepoint — expose the ScopedStorageService
        // decorator wrapping the concrete provider. Off (no axis registered) ⇒ the decorator passes through.
        services.AddSingleton<StorageService>();
        services.AddSingleton<IStorageService>(sp => new ScopedStorageService(sp.GetRequiredService<StorageService>()));
        return services;
    }
}
