using Koan.Admin.Extensions;
using Koan.Admin.Infrastructure;
using Koan.Admin.Options;
using Koan.Web.Admin.Controllers;
using Koan.Web.Admin.Infrastructure;
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
