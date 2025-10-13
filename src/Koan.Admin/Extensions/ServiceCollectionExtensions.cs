using Koan.Admin.Options;
using Koan.Admin.Services;
using Koan.Core.Modules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Koan.Admin.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKoanAdminCore(this IServiceCollection services)
    {
        if (services.Any(d => d.ServiceType == typeof(IKoanAdminFeatureManager)))
        {
            return services;
        }

        services.AddKoanOptions<KoanAdminOptions>(Infrastructure.ConfigurationConstants.Admin.Section)
            .Services.AddSingleton<IValidateOptions<KoanAdminOptions>, KoanAdminOptionsValidator>();

        services.TryAddSingleton<IKoanAdminRouteProvider, KoanAdminRouteProvider>();
        services.TryAddSingleton<IKoanAdminFeatureManager, KoanAdminFeatureManager>();
        services.TryAddSingleton<IKoanAdminManifestService, KoanAdminManifestService>();

        return services;
    }
}
