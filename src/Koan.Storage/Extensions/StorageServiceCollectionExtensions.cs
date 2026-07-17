using Microsoft.Extensions.DependencyInjection;
using Koan.Core.Modules;
using Koan.Storage.Abstractions;

namespace Koan.Storage.Extensions;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Koan.Core;
using Koan.Storage;
using Koan.Storage.Infrastructure;
using Koan.Storage.Identity;
using Koan.Storage.Options;

public static class StorageServiceCollectionExtensions
{
    public static IServiceCollection AddKoanStorage(this IServiceCollection services, IConfiguration config)
    {
        services.AddLogging();
        services.AddKoanOptions<StorageOptions>(config, StorageConstants.Constants.Configuration.Section);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            Koan.Core.Semantics.Segmentation.ISegmentationRealization,
            StorageIdentityPlan>());
        services.TryAddSingleton(sp => sp
            .GetServices<Koan.Core.Semantics.Segmentation.ISegmentationRealization>()
            .OfType<StorageIdentityPlan>()
            .Single());
        // IStorageService is Storage's physical-identity chokepoint. With no active segmentation dimensions the
        // decorator is a byte-identical pass-through.
        services.AddSingleton<StorageService>();
        services.AddSingleton<IStorageService>(sp => new ScopedStorageService(
            sp.GetRequiredService<StorageService>(),
            sp.GetRequiredService<StorageIdentityPlan>()));
        return services;
    }
}
