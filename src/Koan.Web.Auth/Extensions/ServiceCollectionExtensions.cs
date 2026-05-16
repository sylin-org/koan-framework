using System;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Koan.Core;
using Koan.Core.Modules;
using Koan.Web.Auth.Contributors;
using Koan.Web.Auth.Domain;
using Koan.Web.Auth.Hosting;
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
        services.AddKoanOptions<AuthLifecycleOptions>(AuthLifecycleOptions.SectionPath);

        services.AddHttpClient();

        services.AddScoped<IProviderRegistry, ProviderRegistry>();
        services.AddScoped<IAuthProviderElection, AuthProviderElection>();
        // Note: external packages may register IAuthProviderContributor instances to augment defaults.

        // Default in-memory stores; apps can replace these via DI with Entity<>-backed implementations.
        services.AddSingleton<IUserStore, InMemoryUserStore>();
        services.AddSingleton<IExternalIdentityStore, InMemoryExternalIdentityStore>();

        // /me projector. Default surfaces the rich shape (email, roles, claims) so SPAs can render
        // role-gated UI on first paint without a probe round-trip. Use TryAdd so a host can swap
        // the projection (e.g. to redact email or omit custom claims) before this call lands.
        services.TryAddSingleton<ICurrentUserProjector, DefaultCurrentUserProjector>();

        // Event-contributor pipeline (WEB-0065). Scan loaded assemblies for IKoanAuthEventContributor
        // implementations and register them as scoped (so they can depend on per-request services such
        // as DB sessions). AuthEventDispatcher composes the list in Priority order. Apps add a
        // contributor by simply implementing the interface — no DI registration call required.
        DiscoverAndRegisterAuthEventContributors(services);
        services.TryAddScoped<AuthEventDispatcher>();
        services.AddHostedService<AuthBootstrapHostedService>();

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
                            var challengeUrl = BuildChallengeUrl(selection.ChallengePath ?? "", returnUrl);
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
                    },
                    // OnSigningIn / OnSigningOut: framework owns these slots. They dispatch through
                    // AuthEventDispatcher to every registered IKoanAuthEventContributor in Priority
                    // order. Applications that previously assigned o.Events.OnSigningIn directly must
                    // migrate that logic into a contributor (see WEB-0065 ADR). Overwriting these via
                    // a later PostConfigure will break the lifecycle pipeline.
                    OnSigningIn = async ctx =>
                    {
                        var principal = ctx.Principal;
                        if (principal?.Identity is not ClaimsIdentity identity) return;

                        var dispatcher = ctx.HttpContext.RequestServices.GetService<AuthEventDispatcher>();
                        if (dispatcher is null) return;

                        var provider = ResolveProviderFromPath(ctx.HttpContext.Request.Path.Value);
                        var signInCtx = new AuthSignInContext
                        {
                            Provider = provider,
                            Identity = identity,
                            Services = ctx.HttpContext.RequestServices,
                            HttpContext = ctx.HttpContext,
                        };
                        await dispatcher.DispatchSignIn(signInCtx, ctx.HttpContext.RequestAborted);

                        if (signInCtx.RejectReason is not null)
                        {
                            // Outer middleware can read the rejection marker from HttpContext.Items to
                            // translate it into a redirect or distinct response.
                            ctx.HttpContext.Items[AuthLifecycleMarkers.SignInRejected] = signInCtx.RejectReason;
                            ctx.Principal = new ClaimsPrincipal(new ClaimsIdentity());
                        }
                    },
                    OnSigningOut = async ctx =>
                    {
                        var dispatcher = ctx.HttpContext.RequestServices.GetService<AuthEventDispatcher>();
                        if (dispatcher is null) return;
                        var userId = ctx.HttpContext.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                            ?? ctx.HttpContext.User?.FindFirst("sub")?.Value;
                        var signOutCtx = new AuthSignOutContext(userId, ctx.HttpContext.RequestServices, ctx.HttpContext);
                        await dispatcher.DispatchSignOut(signOutCtx, ctx.HttpContext.RequestAborted);
                    }
                };
            });
        return services;
    }

    /// <summary>
    /// Scan loaded assemblies for non-abstract <see cref="IKoanAuthEventContributor"/> types and
    /// register each as a scoped service. Follows the same scan-tolerant pattern used elsewhere in
    /// Koan (skips assemblies that fail <see cref="Assembly.GetTypes"/>, swallows individual type
    /// failures). Idempotent: <see cref="ServiceCollectionDescriptorExtensions.TryAddEnumerable(IServiceCollection, ServiceDescriptor)"/>
    /// ensures the same concrete type is not registered twice.
    /// </summary>
    private static void DiscoverAndRegisterAuthEventContributors(IServiceCollection services)
    {
        var contract = typeof(IKoanAuthEventContributor);
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t is not null).Select(t => t!).ToArray(); }
            catch { continue; }

            foreach (var type in types)
            {
                if (type.IsAbstract || type.IsInterface) continue;
                if (!contract.IsAssignableFrom(type)) continue;
                services.TryAddEnumerable(ServiceDescriptor.Scoped(contract, type));
            }
        }
    }

    private static string? ResolveProviderFromPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        // Koan's OAuth callback route is /auth/{provider}/callback. Other sign-in paths (cookie refresh,
        // programmatic SignInAsync) may not match — provider is null there, which is fine.
        var match = AuthLifecycleConstants.CallbackPathRegex.Match(path);
        return match.Success ? match.Groups[1].Value : null;
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
            var qs = context.Request.QueryString.HasValue ? context.Request.QueryString.Value : "";
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
