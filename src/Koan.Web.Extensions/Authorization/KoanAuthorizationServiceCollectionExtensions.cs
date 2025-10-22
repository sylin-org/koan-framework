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

        if (developmentClaimsTransformer is not null)
        {
            services.AddHttpContextAccessor();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IClaimsTransformation>(sp =>
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
