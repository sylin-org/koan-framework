using System;
using System.Linq;
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
        services.TryAddScoped<ICapabilityAuthorizer, CapabilityAuthorizer>();
        return services;
    }
}