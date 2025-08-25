using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sora.Web.Auth.Options;

namespace Sora.Web.Auth.Extensions;

public static class AuthenticationExtensions
{
    public const string CookieScheme = "sora.cookie";

    public static AuthenticationBuilder AddSoraWebAuthAuthentication(this IServiceCollection services, IConfiguration config)
    {
        var bound = new AuthOptions();
        config.GetSection(AuthOptions.SectionPath).Bind(bound);

        var builder = services.AddAuthentication(options =>
            {
                options.DefaultScheme = CookieScheme;
            })
            .AddCookie(CookieScheme, o =>
            {
                o.Cookie.HttpOnly = true;
                // UseSameAsRequest supports HTTP in Dev/containers while remaining secure on HTTPS
                o.Cookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.SameAsRequest;
                o.SlidingExpiration = true;
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
                                using var user = System.Text.Json.JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ctx.HttpContext.RequestAborted));
                                ctx.RunClaimActions(user.RootElement);
                            }
                        }
                    };
                });
            }
        }

        return builder;
    }
}
