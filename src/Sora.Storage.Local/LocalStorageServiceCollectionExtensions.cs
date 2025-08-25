using Microsoft.Extensions.DependencyInjection;
using Sora.Core.Modules;
using Sora.Storage.Abstractions;

namespace Sora.Storage.Local;

using Microsoft.Extensions.Configuration;
using Sora.Core;
using Sora.Storage;
using Sora.Storage.Local.Infrastructure;

public static class LocalStorageServiceCollectionExtensions
{
    public static IServiceCollection AddSoraLocalStorageProvider(this IServiceCollection services, IConfiguration config)
    {
        // Bind from provided configuration to honor test/app settings
        services.AddSoraOptions<LocalStorageOptions>(config, LocalStorageConstants.Configuration.Section);
        services.AddSingleton<IStorageProvider, LocalStorageProvider>();
        return services;
    }
}
