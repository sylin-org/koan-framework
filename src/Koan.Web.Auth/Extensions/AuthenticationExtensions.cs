using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

                // Avoid HTML redirects for XHR/API callers (send proper 401/403)
                o.Events = new CookieAuthenticationEvents
                {
                    OnRedirectToLogin = ctx =>
                    {
                        if (WantsJson(ctx.Request))
                        {
                            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                            return Task.CompletedTask;
                        }
                        ctx.Response.Redirect(ctx.RedirectUri);
                        return Task.CompletedTask;
                    },
                    OnRedirectToAccessDenied = ctx =>
                    {
                        if (WantsJson(ctx.Request))
                        {
                            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                            return Task.CompletedTask;
                        }
                        ctx.Response.Redirect(ctx.RedirectUri);
                        return Task.CompletedTask;
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
                    o.Authority = p.Authority ?? string.Empty;
                    o.ClientId = p.ClientId ?? string.Empty;
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
                    o.AuthorizationEndpoint = p.AuthorizationEndpoint ?? string.Empty;
                    o.TokenEndpoint = p.TokenEndpoint ?? string.Empty;
                    o.UserInformationEndpoint = p.UserInfoEndpoint ?? string.Empty;
                    o.ClientId = p.ClientId ?? string.Empty;
                    o.ClientSecret = p.ClientSecret ?? string.Empty;
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

    private static bool WantsJson(Microsoft.AspNetCore.Http.HttpRequest req)
    {
        // Heuristics: JSON Accept header, API path, or AJAX header
        var accept = req.Headers["Accept"].ToString();
        if (!string.IsNullOrWhiteSpace(accept) && accept.IndexOf("application/json", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        var apiPath = req.Path.HasValue && (req.Path.StartsWithSegments("/api") || req.Path.StartsWithSegments("/.well-known"));
        if (apiPath) return true;
        var xhr = req.Headers["X-Requested-With"].ToString();
        if (!string.IsNullOrWhiteSpace(xhr) && string.Equals(xhr, "XMLHttpRequest", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }
}
