using Microsoft.Extensions.DependencyInjection;
using Koan.Core.Modules;
using Koan.Storage.Abstractions;

namespace Koan.Storage.Local;

using Microsoft.Extensions.Configuration;
using Koan.Core;
using Koan.Storage;
using Koan.Storage.Local.Infrastructure;

public static class LocalStorageServiceCollectionExtensions
{
    public static IServiceCollection AddKoanLocalStorageProvider(this IServiceCollection services, IConfiguration config)
    {
        // Bind from provided configuration to honor test/app settings
        services.AddKoanOptions<LocalStorageOptions>(config, LocalStorageConstants.Configuration.Section);
        services.AddSingleton<IStorageProvider, LocalStorageProvider>();
        return services;
    }
}
