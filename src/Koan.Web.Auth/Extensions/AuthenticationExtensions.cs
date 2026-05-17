using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Koan.Web.Auth.Flow;
using Koan.Web.Auth.Options;
using Newtonsoft.Json.Linq;

namespace Koan.Web.Auth.Extensions;

public static class AuthenticationExtensions
{
    public const string CookieScheme = "Koan.cookie";

    public static AuthenticationBuilder AddKoanWebAuthAuthentication(this IServiceCollection services, IConfiguration config)
    {
        var bound = new AuthOptions();
        config.GetSection(AuthOptions.SectionPath).Bind(bound);

        var builder = services.AddAuthentication(options =>
            {
                options.DefaultScheme = CookieScheme;
                options.DefaultAuthenticateScheme = CookieScheme;
                options.DefaultChallengeScheme = CookieScheme;
                options.DefaultSignInScheme = CookieScheme;
            })
            .AddCookie(CookieScheme, o =>
            {
                o.Cookie.HttpOnly = true;
                // UseSameAsRequest supports HTTP in Dev/containers while remaining secure on HTTPS
                o.Cookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.SameAsRequest;
                o.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax;
                o.Cookie.Path = "/";
                o.Cookie.Name = ".AspNetCore.Koan.cookie";
                o.SlidingExpiration = true;

                // Cookie events fan out through AuthFlowDispatcher → every registered
                // IKoanAuthFlowHandler. JSON-shape detection lives in the built-in
                // JsonChallengeHandler (Flow/Builtin/JsonChallengeHandler.cs); apps can override
                // the heuristic via Koan:Web:Auth:Challenge options or ship a higher-priority
                // handler that marks ResponseHandled to short-circuit.
                o.Events = new CookieAuthenticationEvents
                {
                    OnRedirectToLogin = async ctx =>
                    {
                        var services = ctx.HttpContext.RequestServices;
                        var dispatcher = services.GetService<AuthFlowDispatcher>();
                        var flowCtx = new AuthChallengeContext
                        {
                            HttpContext = ctx.HttpContext,
                            Services = services,
                            DefaultRedirectUri = ctx.RedirectUri,
                            RedirectUri = ctx.RedirectUri,
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
                    }
                };
            });

        foreach (var (id, p) in bound.Providers)
        {
            var type = (p.Type ?? "oidc").ToLowerInvariant();
            var callback = string.IsNullOrWhiteSpace(p.CallbackPath) ? $"/auth/{id}/callback" : p.CallbackPath;

            if (type == "oidc")
            {
                builder.AddOpenIdConnect(id, o =>
                {
                    o.SignInScheme = CookieScheme;
                    o.Authority = p.Authority ?? "";
                    o.ClientId = p.ClientId ?? "";
                    if (!string.IsNullOrEmpty(p.ClientSecret)) o.ClientSecret = p.ClientSecret;
                    o.ResponseType = "code";
                    o.UsePkce = true;
                    o.SaveTokens = false;
                    o.CallbackPath = callback;
                    if (p.Scopes != null)
                    {
                        o.Scope.Clear();
                        foreach (var s in p.Scopes) o.Scope.Add(s);
                    }
                });
            }
            else if (type == "oauth2")
            {
                builder.AddOAuth(id, o =>
                {
                    o.SignInScheme = CookieScheme;
                    o.AuthorizationEndpoint = p.AuthorizationEndpoint ?? "";
                    o.TokenEndpoint = p.TokenEndpoint ?? "";
                    o.UserInformationEndpoint = p.UserInfoEndpoint ?? "";
                    o.ClientId = p.ClientId ?? "";
                    o.ClientSecret = p.ClientSecret ?? "";
                    o.CallbackPath = callback;
                    if (p.Scopes != null)
                    {
                        o.Scope.Clear();
                        foreach (var s in p.Scopes) o.Scope.Add(s);
                    }
                    o.ClaimActions.MapJsonKey(System.Security.Claims.ClaimTypes.NameIdentifier, "id");
                    o.ClaimActions.MapJsonKey(System.Security.Claims.ClaimTypes.Name, "username");
                    o.ClaimActions.MapJsonKey("avatar", "avatar");
                    o.Events = new OAuthEvents
                    {
                        OnCreatingTicket = async ctx =>
                        {
                            if (!string.IsNullOrEmpty(ctx.Options.UserInformationEndpoint))
                            {
                                var req = new HttpRequestMessage(HttpMethod.Get, ctx.Options.UserInformationEndpoint);
                                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ctx.AccessToken);
                                var resp = await ctx.Backchannel.SendAsync(req, ctx.HttpContext.RequestAborted);
                                resp.EnsureSuccessStatusCode();
                                var json = await resp.Content.ReadAsStringAsync(ctx.HttpContext.RequestAborted);
                                var user = JObject.Parse(json);
                                // Map common claims manually for Newtonsoft JObject
                                var sub = (string?)user["sub"] ?? (string?)user["id"];
                                var name = (string?)user["name"] ?? (string?)user["username"];
                                var avatar = (string?)user["avatar"] ?? (string?)user["picture"];
                                if (!string.IsNullOrWhiteSpace(sub)) ctx.Identity!.AddClaim(new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, sub));
                                if (!string.IsNullOrWhiteSpace(name)) ctx.Identity!.AddClaim(new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, name));
                                if (!string.IsNullOrWhiteSpace(avatar)) ctx.Identity!.AddClaim(new System.Security.Claims.Claim("avatar", avatar));
                            }
                        }
                    };
                });
            }
        }

        return builder;
    }

}
