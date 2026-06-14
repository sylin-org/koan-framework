using System.Linq;
using Koan.Web.Admin.Controllers;
using Koan.Web.Admin.Infrastructure;
using Koan.Web.Admin.Options;
using Koan.Web.Admin.Services;
using Koan.Core.Modules;
using Koan.Web.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Koan.Web.Admin.Extensions;

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
        services.TryAddSingleton<IKoanAdminLaunchKitService, KoanAdminLaunchKitService>();

        return services;
    }

    public static IServiceCollection AddKoanWebAdmin(this IServiceCollection services)
    {
        services.AddKoanAdminCore();
        services.AddKoanControllersFrom<KoanAdminStatusController>();
        services.TryAddScoped<KoanAdminAuthorizationFilter>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<MvcOptions>, KoanAdminRouteConventionSetup>());
        services.TryAddEnumerable(ServiceDescriptor.Transient<IConfigureOptions<AuthorizationOptions>, KoanAdminDevelopmentPolicySetup>());
        return services;
    }
}
