using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;
using Sora.Core;
using Sora.Core.Modules;
using Sora.Web.Auth.Domain;
using Sora.Web.Auth.Infrastructure;
using Sora.Web.Auth.Options;
using Sora.Web.Auth.Providers;

namespace Sora.Web.Auth.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSoraWebAuth(this IServiceCollection services)
    {
        // Bind from configuration by section path at runtime (no IConfiguration required here)
        services.AddSoraOptions<AuthOptions>(AuthOptions.SectionPath);

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
                o.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax;
                o.Cookie.Path = "/";
                o.Cookie.Name = ".AspNetCore.sora.cookie";
                o.SlidingExpiration = true;

                // Avoid HTML redirects for XHR/API callers (send proper 401/403)
                o.Events = new CookieAuthenticationEvents
                {
                    OnRedirectToLogin = ctx =>
                    {
                        if (AuthenticationExtensions_WantsJson(ctx.Request))
                        {
                            ctx.Response.StatusCode = Microsoft.AspNetCore.Http.StatusCodes.Status401Unauthorized;
                            return Task.CompletedTask;
                        }
                        ctx.Response.Redirect(ctx.RedirectUri);
                        return Task.CompletedTask;
                    },
                    OnRedirectToAccessDenied = ctx =>
                    {
                        if (AuthenticationExtensions_WantsJson(ctx.Request))
                        {
                            ctx.Response.StatusCode = Microsoft.AspNetCore.Http.StatusCodes.Status403Forbidden;
                            return Task.CompletedTask;
                        }
                        ctx.Response.Redirect(ctx.RedirectUri);
                        return Task.CompletedTask;
                    }
                };
            });
        return services;
    }

    // Minimal copy of the WantsJson heuristic (kept internal to avoid API surfacing)
    private static bool AuthenticationExtensions_WantsJson(Microsoft.AspNetCore.Http.HttpRequest req)
    {
        var accept = req.Headers["Accept"].ToString();
        if (!string.IsNullOrWhiteSpace(accept) && accept.IndexOf("application/json", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        var apiPath = req.Path.HasValue && (req.Path.StartsWithSegments("/api") || req.Path.StartsWithSegments("/.well-known") || string.Equals(req.Path.Value, "/me", StringComparison.OrdinalIgnoreCase));
        if (apiPath) return true;
        var xhr = req.Headers["X-Requested-With"].ToString();
        if (!string.IsNullOrWhiteSpace(xhr) && string.Equals(xhr, "XMLHttpRequest", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }
}
