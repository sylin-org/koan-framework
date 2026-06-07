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

        services.AddCapabilityAuthorization(configureCapabilities ?? (_ => { }));

        // SEC-0001 §8 (Phase 2, 2f): the resource-side IAuthorize seam, registered in PARALLEL with the
        // live IAuthorizationService path (no behaviour change yet). RbacAuthorizer is the Tier-0 in-process
        // RBAC floor; CapabilityAuthorizer is flipped to route through it in 2k. TryAdd so a host can
        // substitute a PDP/ReBAC adapter (§8 ladder).
        services.TryAddSingleton<IAuthorize, RbacAuthorizer>();

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
