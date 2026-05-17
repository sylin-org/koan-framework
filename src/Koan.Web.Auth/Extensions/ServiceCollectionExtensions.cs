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
using Koan.Web.Auth.Flow;
using Koan.Web.Auth.Flow.Builtin;
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
        services.AddKoanOptions<ChallengeOptions>(ChallengeOptions.SectionPath);

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

        // Flow-handler pipeline. Broadens the contributor surface to cover challenge / access-denied
        // / validate-principal in addition to sign-in / sign-out / bootstrap. Built-ins
        // (JsonChallengeHandler) live alongside app-provided handlers in the same auto-discovered
        // list; legacy IKoanAuthEventContributor implementations participate via
        // LegacyAuthContributorAdapter so existing code continues to work unchanged.
        DiscoverAndRegisterAuthFlowHandlers(services);
        services.AddScoped<IKoanAuthFlowHandler, JsonChallengeHandler>();
        services.TryAddScoped<AuthFlowDispatcher>();

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

                // Cookie event slots are framework-owned. Every slot fans out through
                // AuthFlowDispatcher → every registered IKoanAuthFlowHandler (in Priority order),
                // including LegacyAuthContributorAdapter shims for any IKoanAuthEventContributor
                // implementations that have not yet been migrated. Applications must NOT overwrite
                // these slots via a later PostConfigure — they would break the lifecycle pipeline.
                o.Events = new CookieAuthenticationEvents
                {
                    OnRedirectToLogin = async ctx =>
                    {
                        var services = ctx.HttpContext.RequestServices;
                        var dispatcher = services.GetService<AuthFlowDispatcher>();

                        // Resolve a richer default redirect for interactive flows: a provider-aware
                        // challenge URL (so the cookie middleware doesn't dump the user on a generic
                        // /sign-in when a single provider is configured). Handlers see this as the
                        // initial RedirectUri and can rewrite it freely.
                        var defaultRedirect = ctx.RedirectUri;
                        var selection = services.GetService<IAuthProviderElection>()?.Current;
                        if (selection is not null && selection.HasProvider && selection.SupportsInteractiveChallenge)
                        {
                            var returnUrl = ResolveReturnUrl(ctx);
                            defaultRedirect = BuildChallengeUrl(selection.ChallengePath ?? "", returnUrl);
                        }

                        var flowCtx = new AuthChallengeContext
                        {
                            HttpContext = ctx.HttpContext,
                            Services = services,
                            DefaultRedirectUri = defaultRedirect,
                            RedirectUri = defaultRedirect,
                        };

                        if (dispatcher is not null)
                            await dispatcher.DispatchChallenge(flowCtx, ctx.HttpContext.RequestAborted);

                        if (!flowCtx.ResponseHandled)
                            ctx.Response.Redirect(flowCtx.RedirectUri);
                    },
                    OnRedirectToAccessDenied = async ctx =>
                    {
                        var services = ctx.HttpContext.RequestServices;
                        var dispatcher = services.GetService<AuthFlowDispatcher>();

                        var flowCtx = new AuthAccessDeniedContext
                        {
                            HttpContext = ctx.HttpContext,
                            Services = services,
                            DefaultRedirectUri = ctx.RedirectUri,
                            RedirectUri = ctx.RedirectUri,
                        };

                        if (dispatcher is not null)
                            await dispatcher.DispatchAccessDenied(flowCtx, ctx.HttpContext.RequestAborted);

                        if (!flowCtx.ResponseHandled)
                            ctx.Response.Redirect(flowCtx.RedirectUri);
                    },
                    OnValidatePrincipal = async ctx =>
                    {
                        var services = ctx.HttpContext.RequestServices;
                        var dispatcher = services.GetService<AuthFlowDispatcher>();
                        if (dispatcher is null) return;
                        var validateCtx = new AuthValidatePrincipalContext
                        {
                            Inner = ctx,
                            Services = services,
                        };
                        await dispatcher.DispatchValidatePrincipal(validateCtx, ctx.HttpContext.RequestAborted);
                    },
                    OnSigningIn = async ctx =>
                    {
                        var principal = ctx.Principal;
                        if (principal?.Identity is not ClaimsIdentity identity) return;

                        var dispatcher = ctx.HttpContext.RequestServices.GetService<AuthFlowDispatcher>();
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
                            ctx.HttpContext.Items[AuthLifecycleMarkers.SignInRejected] = signInCtx.RejectReason;
                            ctx.Principal = new ClaimsPrincipal(new ClaimsIdentity());
                        }
                    },
                    OnSigningOut = async ctx =>
                    {
                        var dispatcher = ctx.HttpContext.RequestServices.GetService<AuthFlowDispatcher>();
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
                // AuthFlowDispatcher consumes IEnumerable<IKoanAuthEventContributor> directly and
                // wraps each instance in a LegacyAuthContributorAdapter at construction time, so
                // no separate flow-handler registration is needed for legacy contributors.
            }
        }
    }

    /// <summary>
    /// Scan loaded assemblies for non-abstract <see cref="IKoanAuthFlowHandler"/> types and register
    /// them as scoped services. Mirrors <see cref="DiscoverAndRegisterAuthEventContributors"/>:
    /// scan-tolerant, idempotent via <see cref="ServiceCollectionDescriptorExtensions.TryAddEnumerable"/>.
    /// </summary>
    private static void DiscoverAndRegisterAuthFlowHandlers(IServiceCollection services)
    {
        var contract = typeof(IKoanAuthFlowHandler);
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t is not null).Select(t => t!).ToArray(); }
            catch { continue; }

            foreach (var type in types)
            {
                if (type.IsAbstract || type.IsInterface) continue;
                // LegacyAuthContributorAdapter is a runtime wrapper instantiated by the dispatcher,
                // not a discoverable handler — skip it here so we don't accidentally surface a
                // singleton no-op adapter through the registration.
                if (type == typeof(LegacyAuthContributorAdapter)) continue;
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
