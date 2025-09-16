using Microsoft.Extensions.DependencyInjection;

namespace Koan.Web.Extensions.Authorization;

public static class CapabilityAuthorizationServiceCollectionExtensions
{
    public static IServiceCollection AddCapabilityAuthorization(this IServiceCollection services, Action<CapabilityAuthorizationOptions> configure)
    {
        var opts = new CapabilityAuthorizationOptions();
        configure?.Invoke(opts);
        services.AddSingleton(opts);
        services.AddScoped<ICapabilityAuthorizer, CapabilityAuthorizer>();
        return services;
    }
}