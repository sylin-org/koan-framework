using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Koan.Recipe.Abstractions;

public static class RecipeCapabilities
{
    // True if a service type is already registered
    public static bool ServiceExists(this IServiceCollection services, Type serviceType)
        => services.Any(d => d.ServiceType == serviceType);

    public static bool ServiceExists<TService>(this IServiceCollection services)
        => services.Any(d => d.ServiceType == typeof(TService));

    // True if options TOptions has at least one configure action bound
    public static bool OptionsConfigured<TOptions>(this IServiceCollection services)
        where TOptions : class
    {
        var configure = typeof(IConfigureOptions<TOptions>);
        var post = typeof(IPostConfigureOptions<TOptions>);
        return services.Any(d => configure.IsAssignableFrom(d.ServiceType) || post.IsAssignableFrom(d.ServiceType));
    }
}