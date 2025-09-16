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
        services.AddSingleton<IStorageService, StorageService>();
        return services;
    }
}
