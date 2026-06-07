using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Koan.Web.Extensions.Authorization.Internal;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Koan.Web.Extensions.Authorization;

public static class KoanAuthorizationServiceCollectionExtensions
{
    public static IServiceCollection AddKoanAuthorization(
        this IServiceCollection services,
        Action<AuthorizationOptions>? configurePolicies = null,
        Action<CapabilityAuthorizationOptions>? configureCapabilities = null,
        Func<IServiceProvider, ClaimsPrincipal, Task<ClaimsPrincipal>>? developmentClaimsTransformer = null)
    {
        services.AddAuthorization(options =>
        {
            configurePolicies?.Invoke(options);
        });

        // AddCapabilityAuthorization now also registers the unified IAuthorize seam + provider ladder (SEC-0002),
        // so it is available whether an app calls AddKoanAuthorization or AddCapabilityAuthorization directly.
        services.AddCapabilityAuthorization(configureCapabilities ?? (_ => { }));

        if (developmentClaimsTransformer is not null)
        {
            services.AddHttpContextAccessor();
            // Singleton<TService, TImplementation>(factory) form keeps the descriptor's
            // ImplementationType distinguishable from the service type so TryAddEnumerable can
            // correctly dedup — the plain Singleton<TService>(factory) overload would produce
            // ImplementationType == IClaimsTransformation, which TryAddEnumerable rejects.
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IClaimsTransformation, DelegateClaimsTransformation>(sp =>
                new DelegateClaimsTransformation(principal =>
                {
                    var env = sp.GetRequiredService<IHostEnvironment>();
                    if (!env.IsDevelopment())
                    {
                        return Task.FromResult(principal);
                    }

                    return developmentClaimsTransformer(sp, principal);
                })));
        }

        return services;
    }
}
