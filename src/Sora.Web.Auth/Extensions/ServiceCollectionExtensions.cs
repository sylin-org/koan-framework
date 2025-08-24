using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Sora.Web.Auth.Options;
using Sora.Web.Auth.Providers;
using Sora.Web.Auth.Domain;
using Sora.Web.Auth.Infrastructure;

namespace Sora.Web.Auth.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSoraWebAuth(this IServiceCollection services)
    {
        // Bind from configuration by section path at runtime (no IConfiguration required here)
        services.AddOptions<AuthOptions>().BindConfiguration(AuthOptions.SectionPath).ValidateDataAnnotations();

        services.AddHttpClient();

    services.AddScoped<IProviderRegistry, ProviderRegistry>();
    // Note: external packages may register IAuthProviderContributor instances to augment defaults.

    // Default in-memory stores; apps can replace these via DI with Entity<>-backed implementations.
    services.AddSingleton<IUserStore, InMemoryUserStore>();
    services.AddSingleton<IExternalIdentityStore, InMemoryExternalIdentityStore>();

        // Ensure a default cookie scheme is registered so the centralized challenge/callback can sign users in.
        // External provider handlers (OIDC/OAuth2) are not registered here; flows are handled centrally by AuthController.
        services.AddAuthentication(options =>
            {
                options.DefaultScheme = Extensions.AuthenticationExtensions.CookieScheme;
            })
            .AddCookie(Extensions.AuthenticationExtensions.CookieScheme, o =>
            {
                o.Cookie.HttpOnly = true;
                // Allow HTTP in Development/container scenarios; production should run behind HTTPS/terminator
                o.Cookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.SameAsRequest;
                o.SlidingExpiration = true;
            });
        return services;
    }
}
