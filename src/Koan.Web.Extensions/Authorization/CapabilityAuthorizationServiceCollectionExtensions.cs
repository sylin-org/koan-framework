using System;
using System.Linq;
using Koan.Core.Modules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Koan.Web.Authorization;

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

        return services.AddCapabilityLadder();
    }

    /// <summary>
    /// WEB-0083: what every capability action is decided by, whether or not the application states any policy. The
    /// module registers it with the reference, so an action with no policy is denied rather than falling through to the
    /// seam's allow-by-default; an application's <see cref="AddCapabilityAuthorization(IServiceCollection, Action{CapabilityAuthorizationOptions})"/>
    /// replaces the options whichever side of <c>AddKoan()</c> it stands.
    /// </summary>
    internal static IServiceCollection AddCapabilityDefaults(this IServiceCollection services)
    {
        services.TryAddSingleton(new CapabilityAuthorizationOptions());
        return services.AddCapabilityLadder();
    }

    private static IServiceCollection AddCapabilityLadder(this IServiceCollection services)
    {
        // SEC-0002: capability gates route through the unified IAuthorize seam. PolicyAuthorizationProvider absorbs the
        // WEB-0047 resolution over the options; RbacAuthorizationProvider is the Tier-0 floor. TryAdd → idempotent.
        services.AddKoanOptions<AuthorizeOptions>(AuthorizeOptions.SectionPath);
        services.TryAddScoped<IAuthorize, Authorizer>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthorizationProvider, RbacAuthorizationProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthorizationProvider, PolicyAuthorizationProvider>());
        return services;
    }
}