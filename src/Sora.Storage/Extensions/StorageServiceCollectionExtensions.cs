using Microsoft.Extensions.DependencyInjection;
using Sora.Core.Modules;
using Sora.Storage.Abstractions;

namespace Sora.Storage.Extensions;

using Microsoft.Extensions.Configuration;
using Sora.Core;
using Sora.Storage;
using Sora.Storage.Infrastructure;
using Sora.Storage.Options;

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
