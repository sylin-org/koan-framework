using System;
using System.Linq;
using Koan.Core.Modules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Koan.Web.Extensions.Authorization;

public static class CapabilityAuthorizationServiceCollectionExtensions
{
    public static IServiceCollection AddCapabilityAuthorization(this IServiceCollection services)
        => services.AddCapabilityAuthorization(static _ => { });

    public static IServiceCollection AddCapabilityAuthorization(this IServiceCollection services, Action<CapabilityAuthorizationOptions> configure)
    {
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(CapabilityAuthorizationOptions));
        if (descriptor is not null)
        {
            services.Remove(descriptor);
        }

        var opts = new CapabilityAuthorizationOptions();
        configure?.Invoke(opts);
        services.AddSingleton(opts);

        // SEC-0002: ICapabilityAuthorizer is retired — capability gates route through the unified IAuthorize seam.
        // The seam + capability-graded provider ladder register HERE (not only in AddKoanAuthorization) so the gates
        // enforce whenever capability authz is configured. PolicyAuthorizationProvider absorbs the WEB-0047
        // resolution over the options above; RbacAuthorizationProvider is the Tier-0 floor. TryAdd → idempotent.
        services.AddKoanOptions<AuthorizeOptions>(AuthorizeOptions.SectionPath);
        services.TryAddScoped<IAuthorize, Authorizer>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthorizationProvider, RbacAuthorizationProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthorizationProvider, PolicyAuthorizationProvider>());
        return services;
    }
}