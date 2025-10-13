using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Koan.Core;
using Koan.Core.Modules;
using Koan.Web.Auth.Domain;
using Koan.Web.Auth.Infrastructure;
using Koan.Web.Auth.Options;
using Koan.Web.Auth.Providers;

namespace Koan.Web.Auth.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKoanWebAuth(this IServiceCollection services)
    {
        // Bind from configuration by section path at runtime (no IConfiguration required here)
        services.AddKoanOptions<AuthOptions>(AuthOptions.SectionPath);

        services.AddHttpClient();

        services.AddScoped<IProviderRegistry, ProviderRegistry>();
        services.AddScoped<IAuthProviderElection, AuthProviderElection>();
        // Note: external packages may register IAuthProviderContributor instances to augment defaults.

        // Default in-memory stores; apps can replace these via DI with Entity<>-backed implementations.
        services.AddSingleton<IUserStore, InMemoryUserStore>();
        services.AddSingleton<IExternalIdentityStore, InMemoryExternalIdentityStore>();

        // Ensure a default cookie scheme is registered so the centralized challenge/callback can sign users in.
        // External provider handlers (OIDC/OAuth2) are not registered here; flows are handled centrally by AuthController.
        services.AddAuthentication(options =>
            {
                options.DefaultScheme = Extensions.AuthenticationExtensions.CookieScheme;
                options.DefaultAuthenticateScheme = Extensions.AuthenticationExtensions.CookieScheme;
                options.DefaultChallengeScheme = Extensions.AuthenticationExtensions.CookieScheme;
                options.DefaultSignInScheme = Extensions.AuthenticationExtensions.CookieScheme;
            })
            .AddCookie(Extensions.AuthenticationExtensions.CookieScheme, o =>
            {
                o.Cookie.HttpOnly = true;
                // Allow HTTP in Development/container scenarios; production should run behind HTTPS/terminator
                o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                o.Cookie.SameSite = SameSiteMode.Lax;
                o.Cookie.Path = "/";
                o.Cookie.Name = ".AspNetCore.Koan.cookie";
                o.SlidingExpiration = true;

                // Avoid HTML redirects for XHR/API callers (send proper 401/403)
                o.Events = new CookieAuthenticationEvents
                {
                    OnRedirectToLogin = ctx =>
                    {
                        if (AuthenticationExtensions_WantsJson(ctx.Request))
                        {
                            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                            return Task.CompletedTask;
                        }
                        var selection = ctx.HttpContext.RequestServices.GetService<IAuthProviderElection>()?.Current;
                        if (selection is not null && selection.HasProvider && selection.SupportsInteractiveChallenge)
                        {
                            var returnUrl = ResolveReturnUrl(ctx);
                            var challengeUrl = BuildChallengeUrl(selection.ChallengePath ?? string.Empty, returnUrl);
                            ctx.Response.Redirect(challengeUrl);
                            return Task.CompletedTask;
                        }
                        ctx.Response.Redirect(ctx.RedirectUri);
                        return Task.CompletedTask;
                    },
                    OnRedirectToAccessDenied = ctx =>
                    {
                        if (AuthenticationExtensions_WantsJson(ctx.Request))
                        {
                            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
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
    private static bool AuthenticationExtensions_WantsJson(HttpRequest req)
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

    private static string? ResolveReturnUrl(RedirectContext<CookieAuthenticationOptions> context)
    {
        // Default to the current request path
        string? returnUrl = null;
        if (!string.IsNullOrWhiteSpace(context.RedirectUri))
        {
            var redirectUri = context.RedirectUri;
            if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out var redirect))
            {
                // Treat as relative
                if (!redirectUri.StartsWith('/'))
                {
                    redirectUri = "/" + redirectUri.TrimStart('/');
                }
                if (!Uri.TryCreate("http://localhost" + redirectUri, UriKind.Absolute, out redirect))
                {
                    redirect = null;
                }
            }

            if (redirect is not null)
            {
                var query = QueryHelpers.ParseQuery(redirect.Query);
                if (query.TryGetValue("return", out var rr) && rr.Count > 0)
                {
                    returnUrl = rr[0];
                }
                else if (query.TryGetValue("returnUrl", out var ru) && ru.Count > 0)
                {
                    returnUrl = ru[0];
                }
                else if (query.TryGetValue("ReturnUrl", out var rU) && rU.Count > 0)
                {
                    returnUrl = rU[0];
                }
            }
        }

        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            var path = context.Request.Path.HasValue ? context.Request.Path.Value ?? "/" : "/";
            var qs = context.Request.QueryString.HasValue ? context.Request.QueryString.Value : string.Empty;
            returnUrl = string.IsNullOrWhiteSpace(qs) ? path : path + qs;
        }

        if (Uri.TryCreate(returnUrl, UriKind.Absolute, out var abs))
        {
            if (string.Equals(abs.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) || string.Equals(abs.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                returnUrl = abs.PathAndQuery + abs.Fragment;
            }
            else
            {
                returnUrl = abs.PathAndQuery;
            }
        }

        if (!returnUrl.StartsWith('/'))
        {
            returnUrl = "/" + returnUrl.TrimStart('/');
        }

        return returnUrl;
    }

    private static string BuildChallengeUrl(string challengePath, string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(challengePath))
        {
            return "/auth/test/challenge";
        }

        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return challengePath;
        }

        var separator = challengePath.Contains('?') ? '&' : '?';
        return challengePath + separator + "return=" + Uri.EscapeDataString(returnUrl);
    }
}
