using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Koan.Data.Core;
using Koan.Security.Trust.Issuer;
using Koan.Web.Auth.Providers;
using Koan.Web.Auth.Server.Options;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Koan.Web.Auth.Server.Protocol;

/// <summary>
/// SEC-0006 Phase 2 — the Authorization Code + PKCE flow. Framework-owned protocol endpoints under <c>/oauth/…</c>:
/// <c>/oauth/authorize</c> (validate + create a consent request, redirect to the app's consent page), the
/// <c>/oauth/request/{rid}</c> consent seam (+approve/deny), and <c>/oauth/token</c> (auth-code grant → ES256
/// token). The app owns only page rendering. D4 (bound, single-use, PKCE-mandatory codes), D6 (down-scope; roles
/// from the session, never the request), D7 (consent hardening: browser-bound rid, anti-forgery, clickjacking).
/// </summary>
internal sealed class OAuthProtocolEndpoints : IKoanEndpointContributor
{
    private const string BindingCookiePrefix = "koan.oauth.";

    public void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/oauth/authorize", Authorize).ExcludeFromDescription();
        endpoints.MapGet("/oauth/request/{rid}", GetRequest).ExcludeFromDescription();
        endpoints.MapPost("/oauth/request/{rid}/approve", Approve).ExcludeFromDescription();
        endpoints.MapPost("/oauth/request/{rid}/deny", Deny).ExcludeFromDescription();
        endpoints.MapPost("/oauth/token", Token).ExcludeFromDescription();
    }

    // ---- GET /oauth/authorize ---------------------------------------------------------------------------

    private static async Task Authorize(HttpContext ctx)
    {
        var q = ctx.Request.Query;
        var options = ctx.RequestServices.GetRequiredService<IOptions<AuthServerOptions>>().Value;
        var now = ctx.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();

        var clientId = q["client_id"].ToString();
        var redirectUri = q["redirect_uri"].ToString();

        // Client + redirect_uri are validated BEFORE any redirect — an unvalidated redirect_uri is an open redirect.
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(redirectUri))
        {
            await JsonError(ctx, StatusCodes.Status400BadRequest, "invalid_request", "client_id and redirect_uri are required.");
            return;
        }
        var client = await OAuthClient.Get(clientId, ctx.RequestAborted);
        if (client is null || !client.IsActive(now) || !client.AllowsRedirect(redirectUri))
        {
            await JsonError(ctx, StatusCodes.Status400BadRequest, "invalid_client", "Unknown client or unregistered redirect_uri.");
            return;
        }

        // From here redirect_uri is trusted: protocol errors go back to the client per RFC 6749 §4.1.2.1.
        var state = q["state"].ToString();
        if (!string.Equals(q["response_type"].ToString(), "code", StringComparison.Ordinal))
        {
            RedirectError(ctx, redirectUri, "unsupported_response_type", "Only response_type=code is supported.", state);
            return;
        }
        var challenge = q["code_challenge"].ToString();
        var method = q["code_challenge_method"].ToString();
        if (!Pkce.IsValidChallenge(challenge, method))
        {
            RedirectError(ctx, redirectUri, "invalid_request", "PKCE code_challenge with method=S256 is required.", state);
            return;
        }
        var resource = q["resource"].ToString();
        if (string.IsNullOrWhiteSpace(resource))
        {
            RedirectError(ctx, redirectUri, "invalid_target", "A resource indicator (RFC 8707) is required.", state);
            return;
        }

        var binding = OpaqueToken.New();
        var consent = new ConsentRequest
        {
            Id = OpaqueToken.New(),
            ClientId = clientId,
            RedirectUri = redirectUri,
            CodeChallenge = challenge,
            CodeChallengeMethod = Pkce.MethodS256,
            Scope = q["scope"].ToString(),
            Resource = resource,
            State = string.IsNullOrEmpty(state) ? null : state,
            Nonce = q["nonce"].ToString() is { Length: > 0 } n ? n : null,
            BrowserBinding = binding,
            Status = ConsentRequest.StatusPending,
            ExpiresUtc = now + options.ConsentRequestLifetime,
        };
        await consent.Save(ctx.RequestAborted);

        // Bind to this browser (httpOnly; Lax defeats cross-site POST forgery of the approval).
        ctx.Response.Cookies.Append(BindingCookiePrefix + consent.Id, binding, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = ctx.Request.IsHttps,
            Path = "/oauth",
            MaxAge = options.ConsentRequestLifetime,
        });

        ctx.Response.Redirect(QueryHelpers.AddQueryString(ResolveConsentPath(ctx, options), "rid", consent.Id));
    }

    // The app's consent page path: prefer the AS option (Koan:Web:Auth:Server:ConsentPath), fall back to the
    // MCP-namespaced key the app may have been told to set (Koan:Mcp:Auth:ConsentPath), then the default.
    private static string ResolveConsentPath(HttpContext ctx, AuthServerOptions options)
    {
        var cfg = ctx.RequestServices.GetService<Microsoft.Extensions.Configuration.IConfiguration>();
        return cfg?["Koan:Web:Auth:Server:ConsentPath"]
            ?? cfg?["Koan:Mcp:Auth:ConsentPath"]
            ?? options.ConsentPath;
    }

    // ---- GET /oauth/request/{rid} (the consent seam the app page consumes) ------------------------------

    private static async Task GetRequest(HttpContext ctx, string rid)
    {
        ApplyAntiFraming(ctx);
        var now = ctx.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
        var consent = await ConsentRequest.Get(rid, ctx.RequestAborted);
        if (consent is null || consent.IsExpired(now) || !consent.IsPending)
        {
            await JsonError(ctx, StatusCodes.Status404NotFound, "invalid_request", "Unknown or expired consent request.");
            return;
        }

        var client = await OAuthClient.Get(consent.ClientId, ctx.RequestAborted);
        var registry = ctx.RequestServices.GetService<IProviderRegistry>();
        var providers = registry?.GetDescriptors().Where(d => d.Enabled).ToArray() ?? Array.Empty<ProviderDescriptor>();

        var scopes = consent.Scope
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => new { id = s, description = s })
            .ToArray();

        await ctx.Response.WriteAsJsonAsync(new
        {
            client = new { name = client?.ClientName ?? consent.ClientId, verified = false },
            scopes,
            resource = consent.Resource,
            user = new { loggedIn = ctx.User?.Identity?.IsAuthenticated == true },
            providers,
        }, cancellationToken: ctx.RequestAborted);
    }

    // ---- POST /oauth/request/{rid}/approve ------------------------------------------------------------

    private static async Task Approve(HttpContext ctx, string rid)
    {
        ApplyAntiFraming(ctx);
        var options = ctx.RequestServices.GetRequiredService<IOptions<AuthServerOptions>>().Value;
        var now = ctx.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();

        var consent = await LoadBoundPendingConsent(ctx, rid, now);
        if (consent is null) return; // response already written

        // The subject is the signed-in cookie user; the app must authenticate first (provider pills) and return here.
        if (ctx.User?.Identity?.IsAuthenticated != true)
        {
            await JsonError(ctx, StatusCodes.Status401Unauthorized, "login_required", "Sign in before approving the request.");
            return;
        }

        var subject = Subject(ctx.User);
        if (subject is null)
        {
            await JsonError(ctx, StatusCodes.Status401Unauthorized, "login_required", "The session principal has no subject.");
            return;
        }

        // D6 — roles come from the SESSION (held authority), never the request. Scopes are consented as-requested.
        var granted = consent.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        var code = new AuthorizationCode
        {
            Id = OpaqueToken.New(),
            ClientId = consent.ClientId,
            RedirectUri = consent.RedirectUri,
            CodeChallenge = consent.CodeChallenge,
            Resource = consent.Resource,
            Subject = subject,
            SubjectName = ctx.User.FindFirst(ClaimTypes.Name)?.Value ?? ctx.User.FindFirst(JwtRegisteredClaimNames.Name)?.Value,
            SubjectEmail = ctx.User.FindFirst(ClaimTypes.Email)?.Value ?? ctx.User.FindFirst(JwtRegisteredClaimNames.Email)?.Value,
            Roles = ctx.User.FindAll(ClaimTypes.Role).Select(c => c.Value).Distinct(StringComparer.Ordinal).ToList(),
            GrantedScopes = granted,
            ExpiresUtc = now + options.AuthorizationCodeLifetime,
        };
        await code.Save(ctx.RequestAborted);

        consent.Status = ConsentRequest.StatusApproved;
        await consent.Save(ctx.RequestAborted);
        ClearBinding(ctx, rid);

        var location = QueryHelpers.AddQueryString(consent.RedirectUri, BuildPairs(("code", code.Id), ("state", consent.State)));
        await WriteRedirectResult(ctx, location);
    }

    // ---- POST /oauth/request/{rid}/deny ---------------------------------------------------------------

    private static async Task Deny(HttpContext ctx, string rid)
    {
        ApplyAntiFraming(ctx);
        var now = ctx.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
        var consent = await LoadBoundPendingConsent(ctx, rid, now);
        if (consent is null) return;

        consent.Status = ConsentRequest.StatusDenied;
        await consent.Save(ctx.RequestAborted);
        ClearBinding(ctx, rid);

        var location = QueryHelpers.AddQueryString(consent.RedirectUri, BuildPairs(("error", "access_denied"), ("state", consent.State)));
        await WriteRedirectResult(ctx, location);
    }

    // ---- POST /oauth/token (authorization_code grant) -------------------------------------------------

    private static async Task Token(HttpContext ctx)
    {
        var options = ctx.RequestServices.GetRequiredService<IOptions<AuthServerOptions>>().Value;
        var now = ctx.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
        var form = await ctx.Request.ReadFormAsync(ctx.RequestAborted);

        if (!string.Equals(form["grant_type"].ToString(), "authorization_code", StringComparison.Ordinal))
        {
            await JsonError(ctx, StatusCodes.Status400BadRequest, "unsupported_grant_type", "Only authorization_code is supported.");
            return;
        }

        var codeValue = form["code"].ToString();
        var code = string.IsNullOrEmpty(codeValue) ? null : await AuthorizationCode.Get(codeValue, ctx.RequestAborted);
        if (code is null)
        {
            await JsonError(ctx, StatusCodes.Status400BadRequest, "invalid_grant", "Unknown authorization code.");
            return;
        }

        // Single-use: a replayed (already-consumed) code is rejected. [D9: revoke the issued family — Phase 5.]
        if (code.Consumed || code.IsExpired(now))
        {
            await JsonError(ctx, StatusCodes.Status400BadRequest, "invalid_grant", "Authorization code is expired or already used.");
            return;
        }

        // D4 — re-verify EVERY binding at redemption: client_id, redirect_uri (exact), and PKCE.
        if (!string.Equals(form["client_id"].ToString(), code.ClientId, StringComparison.Ordinal)
            || !string.Equals(form["redirect_uri"].ToString(), code.RedirectUri, StringComparison.Ordinal)
            || !Pkce.VerifyS256(form["code_verifier"].ToString(), code.CodeChallenge))
        {
            await JsonError(ctx, StatusCodes.Status400BadRequest, "invalid_grant", "Authorization code does not match this redemption.");
            return;
        }

        code.Consumed = true;
        await code.Save(ctx.RequestAborted);

        var issuer = ctx.RequestServices.GetRequiredService<IAsymmetricIssuer>();
        var token = issuer.Issue(new TrustClaims
        {
            Subject = code.Subject,
            Name = code.SubjectName,
            Email = code.SubjectEmail,
            Roles = code.Roles,
            Permissions = code.GrantedScopes,
            Extra = new Dictionary<string, IReadOnlyList<string>> { ["client_id"] = new[] { code.ClientId } },
        }, options.AccessTokenLifetime, audience: code.Resource);

        await ctx.Response.WriteAsJsonAsync(new
        {
            access_token = token,
            token_type = "Bearer",
            expires_in = (int)options.AccessTokenLifetime.TotalSeconds,
            scope = string.Join(' ', code.GrantedScopes),
        }, cancellationToken: ctx.RequestAborted);
    }

    // ---- helpers --------------------------------------------------------------------------------------

    private static async Task<ConsentRequest?> LoadBoundPendingConsent(HttpContext ctx, string rid, DateTimeOffset now)
    {
        var consent = await ConsentRequest.Get(rid, ctx.RequestAborted);
        if (consent is null || consent.IsExpired(now) || !consent.IsPending)
        {
            await JsonError(ctx, StatusCodes.Status404NotFound, "invalid_request", "Unknown or expired consent request.");
            return null;
        }
        // D7 — the approval/denial must come from the browser that initiated the request.
        var cookie = ctx.Request.Cookies[BindingCookiePrefix + rid];
        if (string.IsNullOrEmpty(cookie) || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.ASCII.GetBytes(cookie),
                System.Text.Encoding.ASCII.GetBytes(consent.BrowserBinding)))
        {
            await JsonError(ctx, StatusCodes.Status403Forbidden, "invalid_request", "This request was not initiated by this browser.");
            return null;
        }
        return consent;
    }

    private static string? Subject(ClaimsPrincipal user)
        => user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
           ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
           ?? user.FindFirst("sub")?.Value;

    private static Dictionary<string, string?> BuildPairs(params (string Key, string? Value)[] pairs)
    {
        var dict = new Dictionary<string, string?>();
        foreach (var (k, v) in pairs)
            if (!string.IsNullOrEmpty(v)) dict[k] = v;
        return dict;
    }

    private static void RedirectError(HttpContext ctx, string redirectUri, string error, string description, string? state)
    {
        var location = QueryHelpers.AddQueryString(redirectUri,
            BuildPairs(("error", error), ("error_description", description), ("state", state)));
        ctx.Response.Redirect(location);
    }

    // The consent POST result must reach the OAuth client at its redirect_uri (typically a loopback).
    //  - A top-level FORM post → 302, the browser follows to the redirect_uri.
    //  - A SPA fetch()/XHR → 200 { redirect } so the SPA navigates the top window itself (fetch would otherwise
    //    follow the 302 opaquely and the browser would never reach the loopback).
    private static async Task WriteRedirectResult(HttpContext ctx, string location)
    {
        var accept = ctx.Request.Headers.Accept.ToString();
        var isFetch = ctx.Request.Headers.ContainsKey("X-Requested-With")
            || string.Equals(ctx.Request.Headers["Sec-Fetch-Mode"], "cors", StringComparison.OrdinalIgnoreCase)
            || (accept.Contains("application/json", StringComparison.OrdinalIgnoreCase)
                && !accept.Contains("text/html", StringComparison.OrdinalIgnoreCase));

        if (isFetch)
        {
            ctx.Response.StatusCode = StatusCodes.Status200OK;
            await ctx.Response.WriteAsJsonAsync(new { redirect = location }, cancellationToken: ctx.RequestAborted);
        }
        else
        {
            ctx.Response.Redirect(location);
        }
    }

    private static void ClearBinding(HttpContext ctx, string rid)
        => ctx.Response.Cookies.Delete(BindingCookiePrefix + rid, new CookieOptions { Path = "/oauth" });

    private static void ApplyAntiFraming(HttpContext ctx)
    {
        ctx.Response.Headers["X-Frame-Options"] = "DENY";
        ctx.Response.Headers["Content-Security-Policy"] = "frame-ancestors 'none'";
    }

    private static Task JsonError(HttpContext ctx, int status, string error, string description)
    {
        ctx.Response.StatusCode = status;
        return ctx.Response.WriteAsJsonAsync(new { error, error_description = description }, cancellationToken: ctx.RequestAborted);
    }
}
