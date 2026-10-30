using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Hosting.Registry;
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

internal static class ServiceCollectionExtensions
{
    internal static IServiceCollection AddKoanWebAuth(this IServiceCollection services)
    {
        // Bind from configuration by section path at runtime (no IConfiguration required here)
        // WEB-0080: a configured failure landing is a rooted path of this application; anything else refuses the start.
        services.AddKoanOptions<AuthOptions>(AuthOptions.SectionPath)
            .Validate(options => options.ReturnUrl.FailurePath is null || SignInFailureReturn.Accepts(options.ReturnUrl.FailurePath),
                $"{AuthOptions.SectionPath}:ReturnUrl:FailurePath must be a rooted path of this application, such as /sign-in, where a failed sign-in lands; " +
                "it is printable ASCII, names no host, no '.' or '..' segment, no backslash or encoded slash and no fragment, " +
                "and its query does not name 'error', which carries the failure code.")
            // SEC-0029 §2: an allow-list entry names an origin, and optionally a path under it, and nothing else.
            .Validate(options => options.ReturnUrl.AllowList.All(ReturnUrlPolicy.IsAllowListEntry),
                $"{AuthOptions.SectionPath}:ReturnUrl:AllowList entries must each be an absolute http(s) URL naming the origin a return " +
                "may leave the application for, optionally with a path under it, such as https://partner.example.com or " +
                "https://partner.example.com/callback; an entry carries no userinfo, query or fragment.")
            // SEC-0029 §3: the default landing stays in the application unless the allow-list says otherwise.
            .Validate(options => ReturnUrlPolicy.AcceptsDefaultPath(options.ReturnUrl.DefaultPath, options.ReturnUrl.AllowList),
                $"{AuthOptions.SectionPath}:ReturnUrl:DefaultPath must be an application-relative path such as / or /home, which the " +
                "base path is added to; it names no host ('//' or '/\\'), no '.' or '..' segment and no backslash, and is printable " +
                "ASCII. An absolute URL is accepted only under an entry of ReturnUrl:AllowList.")
            // SEC-0025: the resource a token's audience must name is an absolute URI.
            .Validate(options => options.ResourceServer.Resource is null || Uri.TryCreate(options.ResourceServer.Resource, UriKind.Absolute, out _),
                $"{AuthOptions.SectionPath}:ResourceServer:Resource must be the absolute URI a token's audience names, such as https://www.example.com/app; " +
                "remove it to use the application's base URL (Koan:Web:ApplicationUrl).");
        services.AddKoanOptions<AuthLifecycleOptions>(AuthLifecycleOptions.SectionPath);
        services.AddKoanOptions<ChallengeOptions>(ChallengeOptions.SectionPath);

        services.AddHttpClient();

        services.AddSingleton<AuthProviderPlan>();
        services.AddSingleton<IAuthProviderCatalog>(sp => sp.GetRequiredService<AuthProviderPlan>());

        // Default in-memory stores; apps can replace these via DI with Entity<>-backed implementations.
        services.TryAddSingleton<IUserStore, InMemoryUserStore>();
        services.TryAddSingleton<IExternalIdentityStore, InMemoryExternalIdentityStore>();

        // /me projector. Default surfaces the rich shape (email, roles, claims) so SPAs can render
        // role-gated UI on first paint without a probe round-trip. Use TryAdd so a host can swap
        // the projection (e.g. to redact email or omit custom claims) before this call lands.
        services.TryAddSingleton<ICurrentUserProjector, DefaultCurrentUserProjector>();

        // Flow-handler pipeline (WEB-0065/0066). Register every [KoanDiscoverable] IKoanAuthFlowHandler
        // (discovered via KoanRegistry — build-time generator + runtime fallback) as scoped, so they can
        // depend on per-request services such as DB sessions. AuthFlowDispatcher composes the list in
        // Priority order and covers the full lifecycle (sign-in / sign-out / bootstrap / validate-principal
        // / challenge / access-denied). Built-ins (JsonChallengeHandler) live alongside app-provided
        // handlers; apps add a handler by simply implementing the interface — no DI call required.
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
                // WEB-0078: no explicit Path, so the cookie builder scopes it to the request's path base (the
                // application's root under Koan:Web:ApplicationUrl), or "/" when there is none.
                o.Cookie.Name = ".AspNetCore.Koan.cookie";
                o.SlidingExpiration = true;

                // Cookie event slots are framework-owned. Every slot fans out through
                // AuthFlowDispatcher → every registered IKoanAuthFlowHandler (in Priority order).
                // Applications must NOT overwrite these slots via a later PostConfigure — they would
                // break the lifecycle pipeline.
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
                        var selection = services.GetService<AuthProviderPlan>()?.Default;
                        if (selection is not null && selection.Eligible && !string.IsNullOrWhiteSpace(selection.ChallengePath))
                        {
                            var returnUrl = ResolveReturnUrl(ctx);
                            // SEC-0027: Koan's challenge route gets the application's path base exactly once.
                            defaultRedirect = BuildChallengeUrl(ApplicationPaths.Under(ctx.HttpContext, selection.ChallengePath), returnUrl);
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
                        ProviderClaimPolicy.Apply(identity, provider, ctx.HttpContext.RequestServices.GetService<AuthProviderPlan>());
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
                            throw new InvalidOperationException(
                                $"Koan Web Auth sign-in was rejected: {signInCtx.RejectReason}");
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
            })
            // SEC-0025: the resource edge of ordinary requests. Registered always; it becomes the default authenticate,
            // challenge and forbid scheme only when Koan:Web:Auth:ResourceServer:Enabled says so (ResourceServerComposition),
            // and it forwards every request without an Authorization header to the cookie.
            .AddScheme<AuthenticationSchemeOptions, ResourceServerHandler>(
                Extensions.AuthenticationExtensions.ResourceServerScheme,
                o => o.ForwardDefaultSelector = ResourceServerHandler.Forward);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<AuthenticationOptions>, ResourceServerComposition>());
        services.Configure<MvcOptions>(mvc => mvc.Filters.Add<RejectedBearerFilter>());
        return services;
    }

    /// <summary>
    /// Register every discovered <see cref="IKoanAuthFlowHandler"/> as a scoped service. Discovery runs
    /// through <see cref="KoanRegistry.GetDiscoveredImplementors(Type)"/> — populated at build time by the
    /// source generator and at runtime by <c>RegistryManifestLoader</c> from the <c>[KoanDiscoverable]</c>
    /// marker on the interface — so this no longer scans <c>AppDomain</c> assemblies (which miss
    /// lazily-loaded Koan assemblies). Idempotent via
    /// <see cref="ServiceCollectionDescriptorExtensions.TryAddEnumerable(IServiceCollection, ServiceDescriptor)"/>.
    /// </summary>
    private static void DiscoverAndRegisterAuthFlowHandlers(IServiceCollection services)
    {
        var contract = typeof(IKoanAuthFlowHandler);
        foreach (var type in KoanRegistry.GetDiscoveredImplementors(contract))
        {
            services.TryAddEnumerable(ServiceDescriptor.Scoped(contract, type));
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
                if (query.TryGetValue(AuthConstants.Query.Return, out var rr) && rr.Count > 0)
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
            // The page asked for, as the browser addresses it: under the application's path base (SEC-0027).
            var path = context.Request.Path.HasValue ? context.Request.Path.Value ?? "/" : "/";
            var qs = context.Request.QueryString.HasValue ? context.Request.QueryString.Value : "";
            returnUrl = ApplicationPaths.Under(context.HttpContext, string.IsNullOrWhiteSpace(qs) ? path : path + qs);
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

        return WithConfiguredPathBase(context.HttpContext, returnUrl);
    }

    /// <summary>
    /// SEC-0029 §5: the page asked for, named with the path base in its configured spelling. <c>UsePathBase</c> matches
    /// the base without regard to case and keeps the request's spelling, so the cookie handler names
    /// <c>/TANGENT/x</c> for a request to it under <c>/tangent</c>. The authentication cookie is scoped to the configured
    /// spelling and a browser matches a cookie's path by case, so the person must land on <c>/tangent/x</c>, which is
    /// also the spelling the local-path rule accepts. A return that does not begin with the request's path base is
    /// unchanged.
    /// </summary>
    private static string WithConfiguredPathBase(HttpContext context, string returnUrl)
    {
        var requested = context.Request.PathBase.ToUriComponent();
        var configured = ApplicationPaths.PathBase(context).ToUriComponent();
        if (requested.Length == 0 || string.Equals(requested, configured, StringComparison.Ordinal)) return returnUrl;
        if (!returnUrl.StartsWith(requested, StringComparison.Ordinal)
            || returnUrl.Length > requested.Length && returnUrl[requested.Length] is not ('/' or '?' or '#'))
            return returnUrl;
        return configured + returnUrl[requested.Length..];
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
