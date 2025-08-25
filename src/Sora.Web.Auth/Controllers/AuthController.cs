using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sora.Web.Auth.Domain;
using Sora.Web.Auth.Infrastructure;
using Sora.Web.Auth.Options;
using Sora.Web.Auth.Providers;
using Sora.Web.Auth.Extensions;

namespace Sora.Web.Auth.Controllers;

[ApiController]
public sealed class AuthController(IProviderRegistry registry, IHttpClientFactory http, IExternalIdentityStore identities, IOptionsSnapshot<AuthOptions> authOptions, ILogger<AuthController> logger) : ControllerBase
{
    [HttpGet(AuthConstants.Routes.Challenge)]
    public IActionResult Challenge([FromRoute] string provider, [FromQuery(Name = "return")] string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(provider)) return NotFound();
        if (!registry.EffectiveProviders.TryGetValue(provider, out var cfg) || !cfg.Enabled) return NotFound();
        var type = (cfg.Type ?? AuthConstants.Protocols.Oidc).ToLowerInvariant();
        if (type == AuthConstants.Protocols.Saml)
            return Problem(detail: "SAML challenge is not yet implemented.", statusCode: 501);

        // Resolve callback and state
    var callback = string.IsNullOrWhiteSpace(cfg.CallbackPath) ? $"/auth/{provider}/callback" : cfg.CallbackPath!;
    var state = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
    // Use Secure only when the request is HTTPS to support HTTP in dev/container scenarios
    var secure = HttpContext.Request.IsHttps;
    Response.Cookies.Append("sora.auth.state", state, new CookieOptions { HttpOnly = true, Secure = secure, IsEssential = true, SameSite = SameSiteMode.Lax, Expires = DateTimeOffset.UtcNow.AddMinutes(5) });
        var allowed = authOptions.Value.ReturnUrl.AllowList ?? Array.Empty<string>();
        var def = authOptions.Value.ReturnUrl.DefaultPath ?? "/";
        var ru = SanitizeReturnUrl(returnUrl, allowed, def);
    Response.Cookies.Append("sora.auth.return", ru, new CookieOptions { HttpOnly = true, Secure = secure, IsEssential = true, SameSite = SameSiteMode.Lax, Expires = DateTimeOffset.UtcNow.AddMinutes(5) });

    // Optional pass-through hints (e.g., prompt=login) for providers that support it
    var prompt = HttpContext.Request.Query.TryGetValue("prompt", out var p) ? p.ToString() : null;

    if (type == AuthConstants.Protocols.OAuth2)
        {
            // Build authorize URL
            var authz = cfg.AuthorizationEndpoint ?? string.Empty;
            if (string.IsNullOrWhiteSpace(authz)) return Problem(detail: "AuthorizationEndpoint not configured.", statusCode: 500);
            var clientId = cfg.ClientId ?? string.Empty;
            var scope = cfg.Scopes != null && cfg.Scopes.Length > 0 ? string.Join(' ', cfg.Scopes) : string.Empty;
            var redirectUri = BuildAbsolute(callback);
            logger.LogDebug("Auth challenge: provider={Provider} callback={Callback} redirectUri={RedirectUri}", provider, callback, redirectUri);
            var url = $"{authz}?response_type=code&client_id={Uri.EscapeDataString(clientId)}&redirect_uri={Uri.EscapeDataString(redirectUri)}&scope={Uri.EscapeDataString(scope)}&state={Uri.EscapeDataString(state)}";
            if (!string.IsNullOrWhiteSpace(prompt)) url += $"&prompt={Uri.EscapeDataString(prompt)}";
            logger.LogDebug("Auth challenge authorize URL: {AuthorizeUrl}", url);
            return Redirect(url);
        }

        // Minimal OIDC redirect (use authorize at authority)
        var authority = cfg.Authority ?? string.Empty;
        if (string.IsNullOrWhiteSpace(authority)) return Problem(detail: "Authority not configured.", statusCode: 500);
        var client = cfg.ClientId ?? string.Empty;
        var scopeOidc = cfg.Scopes != null && cfg.Scopes.Length > 0 ? string.Join(' ', cfg.Scopes) : "openid profile email";
    var cb = BuildAbsolute(callback);
    logger.LogDebug("OIDC challenge: provider={Provider} callback={Callback} redirectUri={RedirectUri}", provider, callback, cb);
    var authorizeUrl = $"{authority.TrimEnd('/')}/authorize?response_type=code&client_id={Uri.EscapeDataString(client)}&redirect_uri={Uri.EscapeDataString(cb)}&scope={Uri.EscapeDataString(scopeOidc)}&state={Uri.EscapeDataString(state)}";
    if (!string.IsNullOrWhiteSpace(prompt)) authorizeUrl += $"&prompt={Uri.EscapeDataString(prompt)}";
    logger.LogDebug("OIDC challenge authorize URL: {AuthorizeUrl}", authorizeUrl);
        return Redirect(authorizeUrl);
    }

    [HttpGet(AuthConstants.Routes.Callback)]
    public async Task<IActionResult> Callback([FromRoute] string provider, [FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error)
    {
        if (!string.IsNullOrWhiteSpace(error)) return Problem(detail: error, statusCode: 400);
        if (string.IsNullOrWhiteSpace(provider)) return NotFound();
        if (!registry.EffectiveProviders.TryGetValue(provider, out var cfg) || !cfg.Enabled) return NotFound();
        var type = (cfg.Type ?? AuthConstants.Protocols.Oidc).ToLowerInvariant();

        // Validate state
        var expectedState = Request.Cookies["sora.auth.state"]; Response.Cookies.Delete("sora.auth.state");
        if (string.IsNullOrWhiteSpace(state) || string.IsNullOrWhiteSpace(expectedState) || !string.Equals(state, expectedState, StringComparison.Ordinal))
            return Problem(detail: "Invalid state.", statusCode: 400);

        if (string.IsNullOrWhiteSpace(code)) return Problem(detail: "Missing code.", statusCode: 400);

        string? sub = null; string? name = null; string? picture = null; string claimsJson = "{}";
        if (type == AuthConstants.Protocols.OAuth2)
        {
            // Exchange code for token
            var tokenEndpoint = cfg.TokenEndpoint ?? string.Empty;
            if (string.IsNullOrWhiteSpace(tokenEndpoint)) return Problem(detail: "TokenEndpoint not configured.", statusCode: 500);
            // Ensure absolute URL for server-side call
            tokenEndpoint = BuildAbsolute(tokenEndpoint);
            logger.LogDebug("OAuth2 callback: tokenEndpointResolved={TokenEndpoint}", tokenEndpoint);
            var redirectUri = BuildAbsolute(string.IsNullOrWhiteSpace(cfg.CallbackPath) ? $"/auth/{provider}/callback" : cfg.CallbackPath!);
            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code!,
                ["redirect_uri"] = redirectUri,
                ["client_id"] = cfg.ClientId ?? string.Empty,
            };
            if (!string.IsNullOrWhiteSpace(cfg.ClientSecret)) form["client_secret"] = cfg.ClientSecret!;
            var httpClient = http.CreateClient();
            // Defensive: set BaseAddress so relative endpoints still work if misconfigured
            try
            {
                var scheme = string.Equals(HttpContext.Request.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ? Uri.UriSchemeHttps : Uri.UriSchemeHttp;
                var host = HttpContext.Request.Host.HasValue ? HttpContext.Request.Host.Value : "localhost";
                httpClient.BaseAddress = new Uri($"{scheme}://{host}");
            }
            catch { /* ignore */ }
            using var tokenResp = await httpClient.PostAsync(tokenEndpoint, new FormUrlEncodedContent(form), HttpContext.RequestAborted);
            if (!tokenResp.IsSuccessStatusCode) return Problem(detail: $"Token exchange failed: {(int)tokenResp.StatusCode}", statusCode: 502);
            using var tokenDoc = JsonDocument.Parse(await tokenResp.Content.ReadAsStringAsync(HttpContext.RequestAborted));
            var accessToken = tokenDoc.RootElement.TryGetProperty("access_token", out var at) ? at.GetString() : null;
            if (string.IsNullOrWhiteSpace(accessToken)) return Problem(detail: "Token response missing access_token.", statusCode: 502);

            // Fetch user info
            var userInfo = cfg.UserInfoEndpoint ?? string.Empty;
            if (string.IsNullOrWhiteSpace(userInfo)) return Problem(detail: "UserInfoEndpoint not configured.", statusCode: 500);
            // Ensure absolute URL for server-side call
            userInfo = BuildAbsolute(userInfo);
            logger.LogDebug("OAuth2 callback: userInfoEndpointResolved={UserInfoEndpoint}", userInfo);
            var req = new HttpRequestMessage(HttpMethod.Get, userInfo);
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            using var uiResp = await httpClient.SendAsync(req, HttpContext.RequestAborted);
            if (!uiResp.IsSuccessStatusCode) return Problem(detail: $"UserInfo failed: {(int)uiResp.StatusCode}", statusCode: 502);
            var json = await uiResp.Content.ReadAsStringAsync(HttpContext.RequestAborted);
            claimsJson = json;
            using var userDoc = JsonDocument.Parse(json);
            sub = userDoc.RootElement.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            name = userDoc.RootElement.TryGetProperty("username", out var unEl) ? unEl.GetString() : null;
            picture = userDoc.RootElement.TryGetProperty("avatar", out var avEl) ? avEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(sub)) sub = name ?? "user";
        }
        else if (type == AuthConstants.Protocols.Oidc)
        {
            // For now, we do not implement full OIDC token exchange; leave as not implemented.
            return Problem(detail: "OIDC callback is not yet implemented.", statusCode: 501);
        }
        else
        {
            return Problem(detail: "Unsupported provider type.", statusCode: 400);
        }

        // Create principal and sign-in
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, sub ?? Guid.NewGuid().ToString("n")),
        };
        if (!string.IsNullOrWhiteSpace(name)) claims.Add(new Claim(ClaimTypes.Name, name));
        if (!string.IsNullOrWhiteSpace(picture)) claims.Add(new Claim("picture", picture));
    var identity = new ClaimsIdentity(claims, AuthenticationExtensions.CookieScheme);
    await HttpContext.SignInAsync(AuthenticationExtensions.CookieScheme, new ClaimsPrincipal(identity));

        // Persist external identity link (best-effort)
        try
        {
            var userId = claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? sub ?? string.Empty;
            var keyHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(sub ?? string.Empty)));
            await identities.LinkAsync(new ExternalIdentity
            {
                UserId = userId,
                Provider = provider,
                ProviderKeyHash = keyHash,
                ClaimsJson = claimsJson
            }, HttpContext.RequestAborted);
        }
        catch { /* ignore */ }

        var ru = Request.Cookies["sora.auth.return"] ?? authOptions.Value.ReturnUrl.DefaultPath ?? "/";
        Response.Cookies.Delete("sora.auth.return");
        return LocalRedirect(ru);
    }

    [HttpPost(AuthConstants.Routes.Logout)]
    [HttpGet(AuthConstants.Routes.Logout)]
    public async Task<IActionResult> Logout([FromQuery(Name = "return")] string? returnUrl)
    {
        // Sign out of cookie auth
        await HttpContext.SignOutAsync(AuthenticationExtensions.CookieScheme);
    // Best-effort: also clear the local dev TestProvider cookie to avoid silent re-login loops
    try { Response.Cookies.Delete("_tp_user", new CookieOptions { Path = "/" }); } catch { /* ignore */ }
        var allowed = authOptions.Value.ReturnUrl.AllowList ?? Array.Empty<string>();
        var def = authOptions.Value.ReturnUrl.DefaultPath ?? "/";
        var ru = SanitizeReturnUrl(returnUrl, allowed, def);
        return LocalRedirect(ru);
    }

    private static string SanitizeReturnUrl(string? candidate, string[] allowList, string fallback)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return fallback;
        // Allow same-site local paths only, or allow-list prefixes
        if (Uri.TryCreate(candidate, UriKind.Relative, out _)) return candidate;
        if (allowList != null && allowList.Any(p => candidate.StartsWith(p, StringComparison.OrdinalIgnoreCase))) return candidate;
        return fallback;
    }

    private string BuildAbsolute(string relative)
    {
        // If an absolute URI is passed, only accept http/https; otherwise, rebuild as site-relative
        if (Uri.TryCreate(relative, UriKind.Absolute, out var abs))
        {
            if (string.Equals(abs.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(abs.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return abs.ToString();
            }
            // Derive a relative path from the absolute URI (e.g., file:///auth/x -> /auth/x)
            relative = abs.GetComponents(UriComponents.PathAndQuery | UriComponents.Fragment, UriFormat.UriEscaped);
        }
        // Normalize leading slash
        if (!string.IsNullOrEmpty(relative) && !relative.StartsWith('/')) relative = "/" + relative;
        var req = HttpContext.Request;
        // Force to http/https only; avoid accidental non-web schemes
        var scheme = string.Equals(req.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            ? Uri.UriSchemeHttps
            : Uri.UriSchemeHttp;
        var host = req.Host.HasValue ? req.Host.Value : "localhost";
        return $"{scheme}://{host}{relative}";
    }
}
