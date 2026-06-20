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

    // ---- GET /oauth/request/{rid|user_code} (the consent seam — auth-code OR device, unified) ------------

    private static async Task GetRequest(HttpContext ctx, string rid)
    {
        ApplyAntiFraming(ctx);
        var now = ctx.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();

        var consent = await ConsentRequest.Get(rid, ctx.RequestAborted);
        if (consent is not null && !consent.IsExpired(now) && consent.IsPending)
        {
            await WriteConsentContext(ctx, consent.ClientId, consent.Scope, consent.Resource, userCode: null);
            return;
        }

        // Device flow: the id is the typed user_code. Rate-limit so this can't be used to brute-force codes.
        var options = ctx.RequestServices.GetRequiredService<IOptions<AuthServerOptions>>().Value;
        if (!await RateLimitUserCodeAsync(ctx, options, now)) return;
        var device = await FindPendingDevice(rid, now, ctx.RequestAborted);
        if (device is not null)
        {
            await WriteConsentContext(ctx, device.ClientId, device.Scope, device.Resource, userCode: UserCode.Format(device.UserCode));
            return;
        }

        await JsonError(ctx, StatusCodes.Status404NotFound, "invalid_request", "Unknown or expired request.");
    }

    // ---- POST /oauth/request/{rid|user_code}/approve --------------------------------------------------

    private static async Task Approve(HttpContext ctx, string rid)
    {
        ApplyAntiFraming(ctx);
        var options = ctx.RequestServices.GetRequiredService<IOptions<AuthServerOptions>>().Value;
        var now = ctx.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();

        // Auth-code flow (browser-bound rid).
        var consent = await ConsentRequest.Get(rid, ctx.RequestAborted);
        if (consent is not null)
        {
            if (consent.IsExpired(now) || !consent.IsPending) { await JsonError(ctx, StatusCodes.Status404NotFound, "invalid_request", "Unknown or expired request."); return; }
            if (!VerifyBinding(ctx, rid, consent)) { await JsonError(ctx, StatusCodes.Status403Forbidden, "invalid_request", "This request was not initiated by this browser."); return; }
            var sub = await RequireSubjectAsync(ctx);
            if (sub is null) return;

            var granted = consent.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            var code = new AuthorizationCode
            {
                Id = OpaqueToken.New(),
                ClientId = consent.ClientId,
                RedirectUri = consent.RedirectUri,
                CodeChallenge = consent.CodeChallenge,
                Resource = consent.Resource,
                Subject = sub,
                SubjectName = ctx.User!.FindFirst(ClaimTypes.Name)?.Value ?? ctx.User.FindFirst(JwtRegisteredClaimNames.Name)?.Value,
                SubjectEmail = ctx.User.FindFirst(ClaimTypes.Email)?.Value ?? ctx.User.FindFirst(JwtRegisteredClaimNames.Email)?.Value,
                Roles = ctx.User.FindAll(ClaimTypes.Role).Select(c => c.Value).Distinct(StringComparer.Ordinal).ToList(),
                GrantedScopes = granted,
                ExpiresUtc = now + options.AuthorizationCodeLifetime,
            };
            await code.Save(ctx.RequestAborted);
            consent.Status = ConsentRequest.StatusApproved;
            await consent.Save(ctx.RequestAborted);
            ClearBinding(ctx, rid);

            await WriteRedirectResult(ctx, QueryHelpers.AddQueryString(consent.RedirectUri, BuildPairs(("code", code.Id), ("state", consent.State))));
            return;
        }

        // Device flow (user_code). No browser binding (the verifying browser is NOT the device) — the auth session
        // is the authority; rate-limited against user_code brute force (D8).
        if (!await RateLimitUserCodeAsync(ctx, options, now)) return;
        var device = await FindPendingDevice(rid, now, ctx.RequestAborted);
        if (device is null) { await JsonError(ctx, StatusCodes.Status404NotFound, "invalid_request", "Unknown or expired request."); return; }
        var subject = await RequireSubjectAsync(ctx);
        if (subject is null) return;

        device.Status = DeviceCode.StatusApproved;
        device.Subject = subject;
        device.SubjectName = ctx.User!.FindFirst(ClaimTypes.Name)?.Value ?? ctx.User.FindFirst(JwtRegisteredClaimNames.Name)?.Value;
        device.SubjectEmail = ctx.User.FindFirst(ClaimTypes.Email)?.Value ?? ctx.User.FindFirst(JwtRegisteredClaimNames.Email)?.Value;
        device.Roles = ctx.User.FindAll(ClaimTypes.Role).Select(c => c.Value).Distinct(StringComparer.Ordinal).ToList();
        device.GrantedScopes = device.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        await device.Save(ctx.RequestAborted);

        // The device (polling /oauth/token) gets the token; the user's browser just goes to the "you can close" page.
        await WriteRedirectResult(ctx, RequestHost.Url(ctx, ResolveDonePath(ctx, options)));
    }

    // ---- POST /oauth/request/{rid|user_code}/deny -----------------------------------------------------

    private static async Task Deny(HttpContext ctx, string rid)
    {
        ApplyAntiFraming(ctx);
        var options = ctx.RequestServices.GetRequiredService<IOptions<AuthServerOptions>>().Value;
        var now = ctx.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();

        var consent = await ConsentRequest.Get(rid, ctx.RequestAborted);
        if (consent is not null)
        {
            if (consent.IsExpired(now) || !consent.IsPending) { await JsonError(ctx, StatusCodes.Status404NotFound, "invalid_request", "Unknown or expired request."); return; }
            if (!VerifyBinding(ctx, rid, consent)) { await JsonError(ctx, StatusCodes.Status403Forbidden, "invalid_request", "This request was not initiated by this browser."); return; }
            consent.Status = ConsentRequest.StatusDenied;
            await consent.Save(ctx.RequestAborted);
            ClearBinding(ctx, rid);
            await WriteRedirectResult(ctx, QueryHelpers.AddQueryString(consent.RedirectUri, BuildPairs(("error", "access_denied"), ("state", consent.State))));
            return;
        }

        if (!await RateLimitUserCodeAsync(ctx, options, now)) return;
        var device = await FindPendingDevice(rid, now, ctx.RequestAborted);
        if (device is null) { await JsonError(ctx, StatusCodes.Status404NotFound, "invalid_request", "Unknown or expired request."); return; }
        device.Status = DeviceCode.StatusDenied;
        await device.Save(ctx.RequestAborted);
        await WriteRedirectResult(ctx, RequestHost.Url(ctx, ResolveDonePath(ctx, options)));
    }

    // ---- POST /oauth/token (authorization_code | device_code grants) ----------------------------------

    private static async Task Token(HttpContext ctx)
    {
        var form = await ctx.Request.ReadFormAsync(ctx.RequestAborted);
        var grant = form["grant_type"].ToString();
        switch (grant)
        {
            case "authorization_code": await TokenAuthCode(ctx, form); return;
            case "urn:ietf:params:oauth:grant-type:device_code": await TokenDeviceCode(ctx, form); return;
            default: await JsonError(ctx, StatusCodes.Status400BadRequest, "unsupported_grant_type", "Unsupported grant_type."); return;
        }
    }

    private static async Task TokenAuthCode(HttpContext ctx, IFormCollection form)
    {
        var options = ctx.RequestServices.GetRequiredService<IOptions<AuthServerOptions>>().Value;
        var now = ctx.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();

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
        await IssueAccessToken(ctx, options, code.Subject, code.SubjectName, code.SubjectEmail, code.Roles, code.GrantedScopes, code.ClientId, code.Resource);
    }

    private static async Task TokenDeviceCode(HttpContext ctx, IFormCollection form)
    {
        var options = ctx.RequestServices.GetRequiredService<IOptions<AuthServerOptions>>().Value;
        var now = ctx.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();

        var deviceCodeValue = form["device_code"].ToString();
        var device = string.IsNullOrEmpty(deviceCodeValue) ? null : await DeviceCode.Get(deviceCodeValue, ctx.RequestAborted);
        if (device is null || device.Consumed)
        {
            await JsonError(ctx, StatusCodes.Status400BadRequest, "invalid_grant", "Unknown device_code.");
            return;
        }
        if (device.IsExpired(now))
        {
            await JsonError(ctx, StatusCodes.Status400BadRequest, "expired_token", "The device_code has expired.");
            return;
        }

        // RFC 8628 §3.5 — enforce the minimum poll interval (slow_down) before evaluating status.
        if (device.LastPolledUtc is { } last && now - last < TimeSpan.FromSeconds(device.IntervalSeconds))
        {
            device.LastPolledUtc = now;
            await device.Save(ctx.RequestAborted);
            await JsonError(ctx, StatusCodes.Status400BadRequest, "slow_down", "Polling too frequently.");
            return;
        }
        device.LastPolledUtc = now;

        switch (device.Status)
        {
            case DeviceCode.StatusDenied:
                await device.Save(ctx.RequestAborted);
                await JsonError(ctx, StatusCodes.Status400BadRequest, "access_denied", "The user denied the request.");
                return;
            case DeviceCode.StatusPending:
                await device.Save(ctx.RequestAborted);
                await JsonError(ctx, StatusCodes.Status400BadRequest, "authorization_pending", "The user has not yet approved the request.");
                return;
        }

        device.Consumed = true; // single-use
        await device.Save(ctx.RequestAborted);
        await IssueAccessToken(ctx, options, device.Subject ?? "", device.SubjectName, device.SubjectEmail, device.Roles, device.GrantedScopes, device.ClientId, device.Resource);
    }

    private static async Task IssueAccessToken(HttpContext ctx, AuthServerOptions options,
        string subject, string? name, string? email, IReadOnlyCollection<string> roles, IReadOnlyCollection<string> scopes,
        string clientId, string resource)
    {
        var issuer = ctx.RequestServices.GetRequiredService<IAsymmetricIssuer>();
        var token = issuer.Issue(new TrustClaims
        {
            Subject = subject,
            Name = name,
            Email = email,
            Roles = roles,
            Permissions = scopes,
            Extra = new Dictionary<string, IReadOnlyList<string>> { ["client_id"] = new[] { clientId } },
        }, options.AccessTokenLifetime, audience: resource);

        await ctx.Response.WriteAsJsonAsync(new
        {
            access_token = token,
            token_type = "Bearer",
            expires_in = (int)options.AccessTokenLifetime.TotalSeconds,
            scope = string.Join(' ', scopes),
        }, cancellationToken: ctx.RequestAborted);
    }

    // ---- helpers --------------------------------------------------------------------------------------

    // D7 — the approval/denial must come from the browser that initiated the request (constant-time compare).
    private static bool VerifyBinding(HttpContext ctx, string rid, ConsentRequest consent)
    {
        var cookie = ctx.Request.Cookies[BindingCookiePrefix + rid];
        return !string.IsNullOrEmpty(cookie) && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.ASCII.GetBytes(cookie),
            System.Text.Encoding.ASCII.GetBytes(consent.BrowserBinding));
    }

    // The subject is the signed-in cookie user; the app authenticates first (provider pills) and returns here.
    // Returns the subject, or null after writing a 401 (the caller returns).
    private static async Task<string?> RequireSubjectAsync(HttpContext ctx)
    {
        if (ctx.User?.Identity?.IsAuthenticated != true)
        {
            await JsonError(ctx, StatusCodes.Status401Unauthorized, "login_required", "Sign in before approving the request.");
            return null;
        }
        var sub = Subject(ctx.User);
        if (sub is null)
            await JsonError(ctx, StatusCodes.Status401Unauthorized, "login_required", "The session principal has no subject.");
        return sub;
    }

    private static async Task WriteConsentContext(HttpContext ctx, string clientId, string scope, string resource, string? userCode)
    {
        var client = await OAuthClient.Get(clientId, ctx.RequestAborted);
        var registry = ctx.RequestServices.GetService<IProviderRegistry>();
        var providers = registry?.GetDescriptors().Where(d => d.Enabled).ToArray() ?? Array.Empty<ProviderDescriptor>();
        var scopes = scope.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => new { id = s, description = s }).ToArray();

        await ctx.Response.WriteAsJsonAsync(new
        {
            client = new { name = client?.ClientName ?? clientId, verified = false },
            scopes,
            resource,
            user_code = userCode,
            user = new { loggedIn = ctx.User?.Identity?.IsAuthenticated == true },
            providers,
        }, cancellationToken: ctx.RequestAborted);
    }

    private static async Task<DeviceCode?> FindPendingDevice(string userCodeInput, DateTimeOffset now, CancellationToken ct)
    {
        var normalized = UserCode.Normalize(userCodeInput);
        if (normalized.Length == 0) return null;
        var matches = await DeviceCode.Query(d => d.UserCode == normalized, ct);
        return matches.FirstOrDefault(d => d.IsPending && !d.IsExpired(now));
    }

    private static async Task<bool> RateLimitUserCodeAsync(HttpContext ctx, AuthServerOptions options, DateTimeOffset now)
    {
        var limiter = ctx.RequestServices.GetRequiredService<FixedWindowRateLimiter>();
        var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (limiter.TryAcquire("usercode:" + ip, options.UserCodeVerificationRateLimitPerMinute, TimeSpan.FromMinutes(1), now))
            return true;
        await JsonError(ctx, StatusCodes.Status429TooManyRequests, "slow_down", "Too many verification attempts.");
        return false;
    }

    private static string ResolveDonePath(HttpContext ctx, AuthServerOptions options)
    {
        var cfg = ctx.RequestServices.GetService<Microsoft.Extensions.Configuration.IConfiguration>();
        return cfg?["Koan:Web:Auth:Server:DonePath"] ?? cfg?["Koan:Mcp:Auth:DonePath"] ?? options.DonePath;
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
