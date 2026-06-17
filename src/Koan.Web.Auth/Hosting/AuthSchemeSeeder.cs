using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using Koan.Web.Auth.Domain;
using Koan.Web.Auth.Extensions;
using Koan.Web.Auth.Infrastructure;
using Koan.Web.Auth.Options;
using Koan.Web.Auth.Providers;

namespace Koan.Web.Auth.Hosting;

/// <summary>
/// WEB-0071 — registers a maintained ASP.NET <see cref="OAuthHandler{TOptions}"/> /
/// <see cref="OpenIdConnectHandler"/> scheme per effective provider at startup (post-build, full DI),
/// so id_token validation, nonce, PKCE, state, and correlation are owned by the framework rather than
/// hand-rolled. The provider set is composed at runtime (config + <see cref="IAuthProviderContributor"/>
/// defaults + ownerless config ids), which is why this runs centrally and reads
/// <see cref="IProviderRegistry.EffectiveProviders"/> once — see WEB-0071 for the architecture decision.
/// </summary>
/// <remarks>
/// CRITICAL: <see cref="IOptionsMonitorCache{TOptions}.TryAdd"/> caches the instance verbatim and bypasses
/// the configure/post-configure pipeline, so we run the matching <c>*PostConfigureOptions.PostConfigure</c>
/// MANUALLY before seeding — otherwise <c>StateDataFormat</c>/<c>DataProtectionProvider</c>/<c>Backchannel</c>
/// stay null and the handler NPEs at challenge. The three application-policy controls the maintained handler
/// does not own (claim mapping, external-identity link, return-url allow-list) are ported into the handler
/// events here / the challenge entry-point.
/// </remarks>
internal static class AuthSchemeSeeder
{
    private const string ExtraClaimPermission = "Koan.permission";

    /// <summary>Seed schemes from the effective provider set. <paramref name="sp"/> MUST be a scope (the registry is scoped).</summary>
    public static void Seed(IServiceProvider sp)
    {
        var registry = sp.GetRequiredService<IProviderRegistry>();
        var schemes = sp.GetRequiredService<IAuthenticationSchemeProvider>();
        var dp = sp.GetRequiredService<IDataProtectionProvider>();
        var oauthCache = sp.GetRequiredService<IOptionsMonitorCache<OAuthOptions>>();
        var oidcCache = sp.GetRequiredService<IOptionsMonitorCache<OpenIdConnectOptions>>();
        var log = sp.GetService<ILoggerFactory>()?.CreateLogger("Koan.Web.Auth.AuthSchemeSeeder");

        var oauthPost = new OAuthPostConfigureOptions<OAuthOptions, OAuthHandler<OAuthOptions>>(dp);
        var oidcPost = new OpenIdConnectPostConfigureOptions(dp);

        foreach (var (id, cfg) in registry.EffectiveProviders)
        {
            if (!cfg.Enabled) continue;
            var type = (cfg.Type ?? AuthConstants.Protocols.Oidc).ToLowerInvariant();
            // Idempotent: another seeding pass (or a statically-registered scheme) already owns this id.
            if (schemes.GetSchemeAsync(id).GetAwaiter().GetResult() is not null) continue;

            try
            {
                if (type == AuthConstants.Protocols.OAuth2)
                {
                    var opts = BuildOAuthOptions(id, cfg);
                    oauthPost.PostConfigure(id, opts);   // MUST precede TryAdd (see remarks)
                    oauthCache.TryAdd(id, opts);
                    schemes.AddScheme(new AuthenticationScheme(id, cfg.DisplayName ?? id, typeof(OAuthHandler<OAuthOptions>)));
                }
                else if (type == AuthConstants.Protocols.Oidc)
                {
                    var opts = BuildOidcOptions(id, cfg);
                    oidcPost.PostConfigure(id, opts);    // MUST precede TryAdd (see remarks)
                    oidcCache.TryAdd(id, opts);
                    schemes.AddScheme(new AuthenticationScheme(id, cfg.DisplayName ?? id, typeof(OpenIdConnectHandler)));
                }
                else
                {
                    log?.LogDebug("Koan.Web.Auth: provider {Provider} has unsupported type '{Type}'; no scheme seeded", id, type);
                    continue;
                }
                log?.LogDebug("Koan.Web.Auth: seeded {Type} scheme for provider {Provider}", type, id);
            }
            catch (Exception ex)
            {
                // One misconfigured provider must not bring down auth for the others.
                log?.LogWarning(ex, "Koan.Web.Auth: failed to seed scheme for provider {Provider} ({Type})", id, type);
            }
        }
    }

    private static OAuthOptions BuildOAuthOptions(string id, ProviderOptions cfg)
    {
        var o = new OAuthOptions
        {
            SignInScheme = AuthenticationExtensions.CookieScheme,
            // Browser-facing: relative is fine (the browser resolves it). Server-facing endpoints get resolved.
            AuthorizationEndpoint = cfg.AuthorizationEndpoint ?? "",
            TokenEndpoint = ResolveServerAbsolute(cfg.TokenEndpoint) ?? "",
            UserInformationEndpoint = ResolveServerAbsolute(cfg.UserInfoEndpoint) ?? "",
            ClientId = cfg.ClientId ?? "",
            ClientSecret = cfg.ClientSecret ?? "",
            CallbackPath = $"/auth/{id}/callback",
            UsePkce = true, // parity gap: the hand-rolled OAuth2 path shipped NO PKCE
        };
        // WEB-0071: broaden the correlation cookie Path to root so it is returned on the templated
        // /auth/{id}/callback (the default narrows it to the callback path → "Correlation failed").
        o.CorrelationCookie.Path = "/";
        ApplyDevCookiePolicyIfInsecure(o.CorrelationCookie, o.TokenEndpoint);
        if (cfg.Scopes is { Length: > 0 })
        {
            o.Scope.Clear();
            foreach (var s in cfg.Scopes) o.Scope.Add(s);
        }
        o.Events = new OAuthEvents
        {
            OnRedirectToAuthorizationEndpoint = ctx =>
            {
                var url = ctx.RedirectUri;
                if (ctx.Properties.Items.TryGetValue("prompt", out var prompt) && !string.IsNullOrWhiteSpace(prompt))
                    url = Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(url, "prompt", prompt!);
                ctx.Response.Redirect(url);
                return Task.CompletedTask;
            },
            OnCreatingTicket = async ctx =>
            {
                var json = "{}";
                var user = new JObject();
                if (!string.IsNullOrEmpty(ctx.Options.UserInformationEndpoint))
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, ctx.Options.UserInformationEndpoint);
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ctx.AccessToken);
                    using var resp = await ctx.Backchannel.SendAsync(req, ctx.HttpContext.RequestAborted);
                    resp.EnsureSuccessStatusCode();
                    json = await resp.Content.ReadAsStringAsync(ctx.HttpContext.RequestAborted);
                    user = JObject.Parse(json);
                }
                ApplyClaims(ctx.Identity!, user);
                await LinkExternalIdentityAsync(ctx.HttpContext, ctx.Scheme.Name, user, json);
            }
        };
        return o;
    }

    private static OpenIdConnectOptions BuildOidcOptions(string id, ProviderOptions cfg)
    {
        var authority = ResolveServerAbsolute(cfg.Authority) ?? cfg.Authority ?? "";
        var o = new OpenIdConnectOptions
        {
            SignInScheme = AuthenticationExtensions.CookieScheme,
            Authority = authority,
            // Allow http metadata only for a non-https (dev/loopback) authority; production https stays validated.
            RequireHttpsMetadata = authority.StartsWith("https", StringComparison.OrdinalIgnoreCase),
            ClientId = cfg.ClientId ?? "",
            ClientSecret = cfg.ClientSecret,
            ResponseType = "code",
            // Align with the GET authorization-response redirect (the dev Test IdP doesn't form_post).
            ResponseMode = "query",
            UsePkce = true,
            SaveTokens = false,
            CallbackPath = $"/auth/{id}/callback",
            GetClaimsFromUserInfoEndpoint = true, // roles/perms/extras come from userinfo (UserInfoMapper)
        };
        // WEB-0071: broaden the correlation + nonce cookie Path to root so they return on the templated callback.
        o.CorrelationCookie.Path = "/";
        o.NonceCookie.Path = "/";
        ApplyDevCookiePolicyIfInsecure(o.CorrelationCookie, authority);
        ApplyDevCookiePolicyIfInsecure(o.NonceCookie, authority);
        if (cfg.Scopes is { Length: > 0 })
        {
            o.Scope.Clear();
            foreach (var s in cfg.Scopes) o.Scope.Add(s);
        }
        else
        {
            o.Scope.Clear();
            o.Scope.Add("openid");
            o.Scope.Add("profile");
            o.Scope.Add("email");
        }
        o.Events = new OpenIdConnectEvents
        {
            OnRedirectToIdentityProvider = ctx =>
            {
                if (ctx.Properties.Items.TryGetValue("prompt", out var prompt) && !string.IsNullOrWhiteSpace(prompt))
                    ctx.ProtocolMessage.Prompt = prompt;
                return Task.CompletedTask;
            },
            // id_token signature/issuer/audience/nonce/expiry are validated by the handler before this fires.
            OnUserInformationReceived = async ctx =>
            {
                if (ctx.Principal?.Identity is not ClaimsIdentity identity) return;
                var json = ctx.User.RootElement.GetRawText();
                var user = JObject.Parse(json);
                ApplyClaims(identity, user);
                await LinkExternalIdentityAsync(ctx.HttpContext, ctx.Scheme.Name, user, json);
            }
        };
        return o;
    }

    /// <summary>
    /// Port of <see cref="UserInfoMapper"/> + the sub/name/avatar mapping the hand-rolled callback did.
    /// Fail-closed: a missing identifier does NOT mint a synthetic "user" id (that collapsed anonymous logins).
    /// </summary>
    private static void ApplyClaims(ClaimsIdentity identity, JObject user)
    {
        var sub = (string?)user["sub"] ?? (string?)user["id"];
        var name = (string?)user["name"] ?? (string?)user["username"];
        var avatar = (string?)user["avatar"] ?? (string?)user["picture"];

        if (!string.IsNullOrWhiteSpace(sub) && !identity.HasClaim(c => c.Type == ClaimTypes.NameIdentifier))
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, sub));
        if (!string.IsNullOrWhiteSpace(name) && !identity.HasClaim(c => c.Type == ClaimTypes.Name))
            identity.AddClaim(new Claim(ClaimTypes.Name, name));
        if (!string.IsNullOrWhiteSpace(avatar) && !identity.HasClaim(c => c.Type == "avatar"))
            identity.AddClaim(new Claim("avatar", avatar));

        var (roles, perms, extras) = UserInfoMapper.Map(user);
        foreach (var r in roles)
            if (!identity.HasClaim(ClaimTypes.Role, r)) identity.AddClaim(new Claim(ClaimTypes.Role, r));
        foreach (var p in perms)
            if (!identity.HasClaim(ExtraClaimPermission, p)) identity.AddClaim(new Claim(ExtraClaimPermission, p));
        foreach (var kv in extras)
            if (!identity.HasClaim(kv.Key, kv.Value)) identity.AddClaim(new Claim(kv.Key, kv.Value));
    }

    /// <summary>Best-effort external-identity link (SEC-0001). Fail-closed on a missing sub; logged, never swallowed blind.</summary>
    private static async Task LinkExternalIdentityAsync(HttpContext http, string provider, JObject user, string claimsJson)
    {
        var sub = (string?)user["sub"] ?? (string?)user["id"];
        if (string.IsNullOrWhiteSpace(sub)) return; // no identifier → no link (was a SHA256("") collision)

        try
        {
            var store = http.RequestServices.GetService<IExternalIdentityStore>();
            if (store is null) return;
            var keyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sub)));
            await store.Link(new ExternalIdentity
            {
                UserId = sub,
                Provider = provider,
                ProviderKeyHash = keyHash,
                ClaimsJson = claimsJson
            }, http.RequestAborted);
        }
        catch (Exception ex)
        {
            http.RequestServices.GetService<ILoggerFactory>()?
                .CreateLogger("Koan.Web.Auth.AuthSchemeSeeder")
                .LogWarning(ex, "Koan.Web.Auth: external-identity link failed for provider {Provider}; continuing", provider);
        }
    }

    /// <summary>
    /// For an http (dev/loopback) provider, relax the correlation/nonce cookie from the framework default
    /// SameSite=None+Secure (which a browser / HttpClient will not send over plain http → "Correlation failed")
    /// to Lax + SameAsRequest. Same-site dev flows work under Lax; production https providers keep None+Secure.
    /// </summary>
    private static void ApplyDevCookiePolicyIfInsecure(Microsoft.AspNetCore.Http.CookieBuilder cookie, string? endpoint)
    {
        if (endpoint is null || !endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) return;
        cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax;
        cookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.SameAsRequest;
    }

    /// <summary>
    /// The maintained handler's Backchannel calls token/userinfo SERVER-side, so those must be reachable.
    /// Absolute stays as-is; a relative endpoint is prefixed with the in-network base (ASPNETCORE_URLS) when
    /// resolvable, else returned unchanged (best-effort — see WEB-0071 backchannel note).
    /// </summary>
    private static string? ResolveServerAbsolute(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint)) return endpoint;
        if (Uri.TryCreate(endpoint, UriKind.Absolute, out _)) return endpoint;

        var urls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
        if (!string.IsNullOrWhiteSpace(urls))
        {
            var raw = urls.Split([';', ',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault(p => p.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || p.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
            if (raw is not null && Uri.TryCreate(raw, UriKind.Absolute, out var b))
            {
                var scheme = b.Scheme;
                var host = b.Host is "0.0.0.0" or "+" or "*" or "" ? "localhost" : b.Host;
                var port = b.IsDefaultPort ? (scheme == Uri.UriSchemeHttps ? 443 : 80) : b.Port;
                var path = endpoint.StartsWith('/') ? endpoint : "/" + endpoint;
                return $"{scheme}://{host}:{port}{path}";
            }
        }
        return endpoint; // relative; backchannel BaseAddress (if any) resolves it
    }
}
