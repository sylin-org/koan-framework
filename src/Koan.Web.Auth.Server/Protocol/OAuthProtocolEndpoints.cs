using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Koan.Data.Core;
using Koan.Security.Trust.Issuer;
using Koan.Web.Auth.Extensions;
using Koan.Web.Auth.Providers;
using Koan.Web.Auth.Server.Options;
using Koan.Web.Auth.Standing;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
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
/// SEC-0026: authorize, the consent seam, approve and deny read the person from the session scheme explicitly, so a
/// bearer the application's default scheme accepts never signs anyone in here.
/// </summary>
internal static class OAuthProtocolEndpoints
{
    private const string BindingCookiePrefix = "koan.oauth.";

    // ---- GET /oauth/authorize ---------------------------------------------------------------------------

    internal static async Task Authorize(HttpContext ctx)
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
        if (!client.AllowsGrant(OAuthGrantTypes.AuthorizationCode))
        {
            RedirectError(ctx, redirectUri, "unauthorized_client", "This client is not allowed the authorization code grant.", state);
            return;
        }
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
            // RFC 6749 §4.1.2.1 authorization-endpoint error code (invalid_target is a token-endpoint code).
            RedirectError(ctx, redirectUri, "invalid_request", "A resource indicator (RFC 8707) is required.", state);
            return;
        }

        var scopeList = q["scope"].ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        // D9 — remembered consent: a person signed in by their session whose consent to this (client, scope-set,
        // resource) is remembered skips the consent page; the code is minted directly. New scopes, or revoking any of
        // the person's live approvals for it (SEC-0005), re-prompt. SEC-0026: the session alone — a bearer for the
        // person never stands in for the browser.
        var session = await Session(ctx);
        if (options.RememberConsent && session is not null && Subject(session) is { } rememberedSub
            && await OAuthGrants.RemembersAsync(rememberedSub, clientId, scopeList, resource, now, ctx.RequestAborted))
        {
            var remembered = new AuthorizationCode
            {
                Id = OpaqueToken.New(),
                ClientId = clientId,
                RedirectUri = redirectUri,
                CodeChallenge = challenge,
                Resource = resource,
                Subject = rememberedSub,
                SubjectName = session.FindFirst(ClaimTypes.Name)?.Value ?? session.FindFirst(JwtRegisteredClaimNames.Name)?.Value,
                SubjectEmail = session.FindFirst(ClaimTypes.Email)?.Value ?? session.FindFirst(JwtRegisteredClaimNames.Email)?.Value,
                Roles = session.FindAll(ClaimTypes.Role).Select(c => c.Value).Distinct(StringComparer.Ordinal).ToList(),
                GrantedScopes = scopeList,
                Remembered = true,
                ExpiresUtc = now + options.AuthorizationCodeLifetime,
            };
            await remembered.Save(ctx.RequestAborted);
            ctx.Response.Redirect(QueryHelpers.AddQueryString(redirectUri, BuildPairs(("code", remembered.Id), ("state", state))));
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

        // Bind to this browser (httpOnly; Lax defeats cross-site POST forgery of the approval). SEC-0027: the cookie and
        // the consent page sit under the application's path base, so the approval under that path carries the cookie.
        ctx.Response.Cookies.Append(BindingCookiePrefix + consent.Id, binding, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = ctx.Request.IsHttps,
            Path = AppPaths.BindingCookie(ctx),
            MaxAge = options.ConsentRequestLifetime,
        });

        ctx.Response.Redirect(QueryHelpers.AddQueryString(AppPaths.Under(ctx, AppPaths.Consent(ctx, options)), "rid", consent.Id));
    }

    // ---- GET /oauth/request/{rid|user_code} (the consent seam — auth-code OR device, unified) ------------

    internal static async Task GetRequest(HttpContext ctx, string rid)
    {
        ApplyAntiFraming(ctx);
        if (await RejectOversizedId(ctx, rid)) return;

        // SEC-0028: the same lookup an application's server-rendered page makes — a rid, else a typed user code
        // (normalised, rate-limited so this can't be used to brute-force codes).
        var lookup = await ConsentPage.Find(ctx, rid, ctx.RequestAborted);
        if (lookup.Throttled) { await TooManyAttempts(ctx); return; }
        if (lookup.ClientNotAllowed) { await DeviceGrantRefused(ctx); return; }
        if (lookup.Request is not { } pending)
        {
            await JsonError(ctx, StatusCodes.Status404NotFound, "invalid_request", "Unknown or expired request.");
            return;
        }
        await WriteConsentContext(ctx, pending);
    }

    // ---- POST /oauth/request/{rid|user_code}/approve --------------------------------------------------

    internal static async Task Approve(HttpContext ctx, string rid)
    {
        ApplyAntiFraming(ctx);
        if (await RejectOversizedId(ctx, rid)) return;
        var options = ctx.RequestServices.GetRequiredService<IOptions<AuthServerOptions>>().Value;
        var now = ctx.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();

        // Auth-code flow (browser-bound rid).
        var consent = await ConsentRequest.Get(rid, ctx.RequestAborted);
        if (consent is not null)
        {
            if (consent.IsExpired(now) || !consent.IsPending) { await JsonError(ctx, StatusCodes.Status404NotFound, "invalid_request", "Unknown or expired request."); return; }
            if (!VerifyBinding(ctx, rid, consent)) { await JsonError(ctx, StatusCodes.Status403Forbidden, "invalid_request", "This request was not initiated by this browser."); return; }
            if (await RequireSession(ctx) is not { } consenting) return;
            var (sub, person) = consenting;

            var granted = consent.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            var code = new AuthorizationCode
            {
                Id = OpaqueToken.New(),
                ClientId = consent.ClientId,
                RedirectUri = consent.RedirectUri,
                CodeChallenge = consent.CodeChallenge,
                Resource = consent.Resource,
                Subject = sub,
                SubjectName = person.FindFirst(ClaimTypes.Name)?.Value ?? person.FindFirst(JwtRegisteredClaimNames.Name)?.Value,
                SubjectEmail = person.FindFirst(ClaimTypes.Email)?.Value ?? person.FindFirst(JwtRegisteredClaimNames.Email)?.Value,
                Roles = person.FindAll(ClaimTypes.Role).Select(c => c.Value).Distinct(StringComparer.Ordinal).ToList(),
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
        var device = await ConsentPage.FindPendingDevice(rid, now, ctx.RequestAborted);
        if (device is null) { await JsonError(ctx, StatusCodes.Status404NotFound, "invalid_request", "Unknown or expired request."); return; }
        // SEC-0024: the client must still be allowed the device grant when the person decides, not only when it asked.
        if (!await OAuthClient.Allows(device.ClientId, OAuthGrantTypes.DeviceCode, now, ctx.RequestAborted)) { await DeviceGrantRefused(ctx); return; }
        if (await RequireSession(ctx) is not { } signedIn) return;
        var (subject, approver) = signedIn;

        device.Status = DeviceCode.StatusApproved;
        device.Subject = subject;
        device.SubjectName = approver.FindFirst(ClaimTypes.Name)?.Value ?? approver.FindFirst(JwtRegisteredClaimNames.Name)?.Value;
        device.SubjectEmail = approver.FindFirst(ClaimTypes.Email)?.Value ?? approver.FindFirst(JwtRegisteredClaimNames.Email)?.Value;
        device.Roles = approver.FindAll(ClaimTypes.Role).Select(c => c.Value).Distinct(StringComparer.Ordinal).ToList();
        device.GrantedScopes = device.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        await device.Save(ctx.RequestAborted);

        // The device (polling /oauth/token) gets the token; the user's browser goes to the "you can close" page, which
        // can tell what happened (SEC-0028).
        await WriteRedirectResult(ctx, ConsentPage.DoneUrl(ctx, options, ConsentOutcome.Approved, subject));
    }

    // ---- POST /oauth/request/{rid|user_code}/deny -----------------------------------------------------

    internal static async Task Deny(HttpContext ctx, string rid)
    {
        ApplyAntiFraming(ctx);
        if (await RejectOversizedId(ctx, rid)) return;
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
        var device = await ConsentPage.FindPendingDevice(rid, now, ctx.RequestAborted);
        if (device is null) { await JsonError(ctx, StatusCodes.Status404NotFound, "invalid_request", "Unknown or expired request."); return; }
        // SEC-0028: a person decides a device request either way, so every outcome the done page reads names them.
        if (await RequireSession(ctx) is not { } denying) return;
        device.Status = DeviceCode.StatusDenied;
        await device.Save(ctx.RequestAborted);
        await WriteRedirectResult(ctx, ConsentPage.DoneUrl(ctx, options, ConsentOutcome.Denied, denying.Subject));
    }

    // ---- POST /oauth/token (authorization_code | device_code grants) ----------------------------------

    internal static async Task Token(HttpContext ctx)
    {
        var form = await ctx.Request.ReadFormAsync(ctx.RequestAborted);
        var grant = form["grant_type"].ToString();
        switch (grant)
        {
            case OAuthGrantTypes.AuthorizationCode: await TokenAuthCode(ctx, form); return;
            case OAuthGrantTypes.DeviceCode: await TokenDeviceCode(ctx, form); return;
            case OAuthGrantTypes.RefreshToken: await TokenRefresh(ctx, form); return;
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

        if (code.IsExpired(now))
        {
            await JsonError(ctx, StatusCodes.Status400BadRequest, "invalid_grant", "Authorization code has expired.");
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

        if (await Current(ctx, new GrantedIdentity(code.Subject, code.SubjectName, code.SubjectEmail, code.Roles, code.GrantedScopes)) is not { } id) return;

        // D4 single-use — atomic claim: delete-by-id is atomic on every adapter, so exactly one of any concurrent
        // redemptions wins (no read-check-write double-spend). The loser sees the row already gone.
        if (!await code.Remove(ctx.RequestAborted))
        {
            await JsonError(ctx, StatusCodes.Status400BadRequest, "invalid_grant", "Authorization code already used.");
            return;
        }

        var grantId = await IssueGrant(options, id, code.ClientId, code.Resource, consented: !code.Remembered, now, ctx.RequestAborted);
        await IssueTokens(ctx, options, id, code.ClientId, code.Resource, OpaqueToken.New(), grantId, now);
    }

    private static async Task TokenDeviceCode(HttpContext ctx, IFormCollection form)
    {
        var options = ctx.RequestServices.GetRequiredService<IOptions<AuthServerOptions>>().Value;
        var now = ctx.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();

        var deviceCodeValue = form["device_code"].ToString();
        var device = string.IsNullOrEmpty(deviceCodeValue) ? null : await DeviceCode.Get(deviceCodeValue, ctx.RequestAborted);
        if (device is null)
        {
            await JsonError(ctx, StatusCodes.Status400BadRequest, "invalid_grant", "Unknown device_code.");
            return;
        }
        // RFC 8628 §3.4 — the token request must present the same client_id as the device authorization request.
        if (!string.Equals(form["client_id"].ToString(), device.ClientId, StringComparison.Ordinal))
        {
            await JsonError(ctx, StatusCodes.Status400BadRequest, "invalid_grant", "client_id does not match the device authorization.");
            return;
        }
        // SEC-0024 / RFC 6749 §5.2: a device code is exchanged only for a client still allowed the device grant, whatever
        // was decided while it had it.
        if (!await OAuthClient.Allows(device.ClientId, OAuthGrantTypes.DeviceCode, now, ctx.RequestAborted))
        {
            await DeviceGrantRefused(ctx);
            return;
        }
        if (device.IsExpired(now))
        {
            await JsonError(ctx, StatusCodes.Status400BadRequest, "expired_token", "The device_code has expired.");
            return;
        }

        // RFC 8628 §3.5 — enforce the minimum poll interval (slow_down) before evaluating status, and bump the
        // interval each time a client polls too fast (the conformant back-off ladder).
        if (device.LastPolledUtc is { } last && now - last < TimeSpan.FromSeconds(device.IntervalSeconds))
        {
            device.IntervalSeconds += 5;
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

        if (await Current(ctx, new GrantedIdentity(device.Subject ?? "", device.SubjectName, device.SubjectEmail, device.Roles, device.GrantedScopes)) is not { } id) return;

        // Single-use — atomic claim (delete-by-id), so concurrent polls of an approved device can't double-issue.
        if (!await device.Remove(ctx.RequestAborted))
        {
            await JsonError(ctx, StatusCodes.Status400BadRequest, "invalid_grant", "The device_code has already been used.");
            return;
        }

        var grantId = await IssueGrant(options, id, device.ClientId, device.Resource, consented: true, now, ctx.RequestAborted);
        await IssueTokens(ctx, options, id, device.ClientId, device.Resource, OpaqueToken.New(), grantId, now);
    }

    // ---- refresh_token grant (D9: rotation + reuse-detection + revocation) ----------------------------

    private static async Task TokenRefresh(HttpContext ctx, IFormCollection form)
    {
        var options = ctx.RequestServices.GetRequiredService<IOptions<AuthServerOptions>>().Value;
        var now = ctx.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();

        var raw = form["refresh_token"].ToString();
        var rt = string.IsNullOrEmpty(raw) ? null : await RefreshToken.Get(OAuthGrants.HashToken(raw), ctx.RequestAborted);
        if (rt is null)
        {
            await JsonError(ctx, StatusCodes.Status400BadRequest, "invalid_grant", "Unknown refresh token.");
            return;
        }

        // OAuth 2.1 §4.3.1 — a replayed (already-rotated) token is reuse: revoke the whole family + the grant.
        if (rt.Rotated)
        {
            await OAuthGrants.RevokeFamilyAsync(rt.FamilyId, ctx.RequestAborted);
            await OAuthGrants.RevokeGrantAsync(rt.GrantId, ctx.RequestAborted);
            await JsonError(ctx, StatusCodes.Status400BadRequest, "invalid_grant", "Refresh token reuse detected; the session has been revoked.");
            return;
        }
        if (rt.IsExpired(now))
        {
            await JsonError(ctx, StatusCodes.Status400BadRequest, "invalid_grant", "Refresh token has expired.");
            return;
        }
        // No client substitution; the backing grant must still be live (revocation fails closed).
        if (!string.Equals(form["client_id"].ToString(), rt.ClientId, StringComparison.Ordinal)
            || !await OAuthGrants.GrantLiveAsync(rt.GrantId, now, ctx.RequestAborted))
        {
            await JsonError(ctx, StatusCodes.Status400BadRequest, "invalid_grant", "The grant is no longer valid.");
            return;
        }

        // Before rotation: a refused token is not spent, so an agent refused while its person is timed out resumes when the
        // time-out ends, and a store that cannot answer costs the client nothing.
        if (await Current(ctx, new GrantedIdentity(rt.Subject, rt.SubjectName, rt.SubjectEmail, rt.Roles, rt.GrantedScopes)) is not { } id) return;

        rt.Rotated = true; // rotate — this token is now spent
        await rt.Save(ctx.RequestAborted);

        await IssueTokens(ctx, options, id, rt.ClientId, rt.Resource, rt.FamilyId, rt.GrantId, now);
    }

    /// <summary>
    /// SEC-0005 — the grant behind a new refresh family, one per approval, remembering the person's consent when they
    /// gave it on the consent page. With refresh tokens off no token would name a grant, so none is issued and no
    /// consent is remembered: answers the empty grant id.
    /// </summary>
    private static async Task<string> IssueGrant(AuthServerOptions options, GrantedIdentity id, string clientId,
        string resource, bool consented, DateTimeOffset now, CancellationToken ct)
    {
        if (!options.EnableRefreshTokens) return "";
        var grant = await OAuthGrants.IssueAsync(id.Subject, clientId, id.Scopes, resource, now,
            options.RefreshTokenLifetime, remember: consented && options.RememberConsent, ct);
        return grant.Id;
    }

    internal sealed record GrantedIdentity(string Subject, string? Name, string? Email, List<string> Roles, List<string> Scopes);

    /// <summary>
    /// Who a token is minted for now, not when they approved: the subject's standing replaces what was captured then, and
    /// what no contributor speaks to stands as captured (a host with no owner of its people has only the approval). A
    /// subject who may not act gets no token: writes <c>invalid_grant</c> and answers null. The reason stays the
    /// application's; a client learns only that the grant no longer works.
    /// </summary>
    private static async Task<GrantedIdentity?> Current(HttpContext ctx, GrantedIdentity captured)
    {
        if (await Standing(ctx.RequestServices, captured, ctx.RequestAborted) is { } current) return current;
        await JsonError(ctx, StatusCodes.Status400BadRequest, "invalid_grant", "The grant is no longer valid.");
        return null;
    }

    /// <summary>The subject as their standing has them now, or null when they may not act.</summary>
    internal static async Task<GrantedIdentity?> Standing(IServiceProvider services, GrantedIdentity captured, CancellationToken ct)
    {
        var standing = await services.GetRequiredService<SubjectStandings>().Of(captured.Subject, ct);
        if (standing is null) return captured;
        if (!standing.Active) return null;

        return captured with
        {
            Name = standing.Name ?? captured.Name,
            Roles = standing.Roles is { } roles ? [.. roles.Order(StringComparer.Ordinal)] : captured.Roles,
        };
    }

    private static async Task IssueTokens(HttpContext ctx, AuthServerOptions options, GrantedIdentity id,
        string clientId, string resource, string familyId, string grantId, DateTimeOffset now)
    {
        var access = Mint(ctx.RequestServices, options, id, clientId, resource);

        var body = new Dictionary<string, object?>
        {
            ["access_token"] = access,
            ["token_type"] = "Bearer",
            ["expires_in"] = (int)options.AccessTokenLifetime.TotalSeconds,
            ["scope"] = string.Join(' ', id.Scopes),
        };

        // RFC 6749 §5.1 — token responses must not be cached by intermediaries.
        ctx.Response.Headers.CacheControl = "no-store";
        ctx.Response.Headers.Pragma = "no-cache";

        if (options.EnableRefreshTokens)
        {
            var rawRefresh = OpaqueToken.New();
            var refresh = new RefreshToken
            {
                Id = OAuthGrants.HashToken(rawRefresh),
                FamilyId = familyId,
                ClientId = clientId,
                Resource = resource,
                Subject = id.Subject,
                SubjectName = id.Name,
                SubjectEmail = id.Email,
                Roles = id.Roles,
                GrantedScopes = id.Scopes,
                GrantId = grantId,
                ExpiresUtc = now + options.RefreshTokenLifetime,
            };
            await refresh.Save(ctx.RequestAborted);
            body["refresh_token"] = rawRefresh;
        }

        await ctx.Response.WriteAsJsonAsync(body, cancellationToken: ctx.RequestAborted);
    }

    /// <summary>The access token of <paramref name="id"/> for <paramref name="resource"/>, for the server's lifetime; a
    /// token the application issued itself names no client.</summary>
    internal static string Mint(IServiceProvider services, AuthServerOptions options, GrantedIdentity id, string? clientId, string resource)
        => services.GetRequiredService<IIssuer>().Issue(new TrustClaims
        {
            Subject = id.Subject,
            Name = id.Name,
            Email = id.Email,
            Roles = id.Roles,
            Scopes = id.Scopes, // the granted scopes are the authorization grant → the RFC 9068 `scope` claim
            Extra = clientId is null ? null : new Dictionary<string, IReadOnlyList<string>> { ["client_id"] = new[] { clientId } },
        }, options.AccessTokenLifetime, audience: resource);

    // ---- helpers --------------------------------------------------------------------------------------

    // D7 — the approval/denial must come from the browser that initiated the request (constant-time compare).
    private static bool VerifyBinding(HttpContext ctx, string rid, ConsentRequest consent)
    {
        var cookie = ctx.Request.Cookies[BindingCookiePrefix + rid];
        if (string.IsNullOrEmpty(cookie)) return false;
        // UTF-8 (not ASCII, which silently substitutes '?' for non-ASCII) + a length guard so FixedTimeEquals
        // never short-circuits on a length mismatch from an attacker-shaped cookie.
        var a = System.Text.Encoding.UTF8.GetBytes(cookie);
        var b = System.Text.Encoding.UTF8.GetBytes(consent.BrowserBinding);
        return a.Length == b.Length && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(a, b);
    }

    // SEC-0026 — the person who decides is the one signed in by their session (Koan.cookie), read explicitly: whatever
    // the application's default scheme produced — a bearer for the same person included — never decides here.
    internal static async Task<ClaimsPrincipal?> Session(HttpContext ctx)
    {
        var result = await ctx.AuthenticateAsync(AuthenticationExtensions.CookieScheme);
        return result.Succeeded && result.Principal?.Identity?.IsAuthenticated == true ? result.Principal : null;
    }

    // The subject is the person signed in by their session; the app authenticates first (provider pills) and returns
    // here. Returns the subject and the session principal, or null after writing a 401 (the caller returns).
    private static async Task<(string Subject, ClaimsPrincipal Person)?> RequireSession(HttpContext ctx)
    {
        if (await Session(ctx) is not { } person)
        {
            await JsonError(ctx, StatusCodes.Status401Unauthorized, "login_required", "Sign in before approving the request.");
            return null;
        }
        if (Subject(person) is not { } sub)
        {
            await JsonError(ctx, StatusCodes.Status401Unauthorized, "login_required", "The session principal has no subject.");
            return null;
        }
        return (sub, person);
    }

    private static async Task WriteConsentContext(HttpContext ctx, PendingConsent pending)
    {
        var catalog = ctx.RequestServices.GetService<IAuthProviderCatalog>();
        var providers = catalog?.Providers
            .Where(static provider => provider.Eligible)
            .Select(provider => new
            {
                id = provider.Id,
                name = provider.DisplayName,
                protocol = provider.Protocol,
                icon = provider.Icon,
                // SEC-0027: a link the consent page renders, so under the application's path base.
                challengeUrl = provider.ChallengePath is { } challenge ? AppPaths.Under(ctx, challenge) : null
            })
            .ToArray() ?? [];
        var scopes = pending.Scopes.Select(s => new { id = s, description = s }).ToArray();

        await ctx.Response.WriteAsJsonAsync(new
        {
            client = new { name = pending.ClientName, verified = false },
            scopes,
            resource = pending.Resource,
            user_code = pending.UserCode,
            user = new { loggedIn = await Session(ctx) is not null },
            providers,
        }, cancellationToken: ctx.RequestAborted);
    }

    private static async Task<bool> RateLimitUserCodeAsync(HttpContext ctx, AuthServerOptions options, DateTimeOffset now)
    {
        if (ConsentPage.TryAcquireUserCode(ctx, options, now)) return true;
        await TooManyAttempts(ctx);
        return false;
    }

    private static Task TooManyAttempts(HttpContext ctx)
        => JsonError(ctx, StatusCodes.Status429TooManyRequests, "slow_down", "Too many verification attempts.");

    private static Task DeviceGrantRefused(HttpContext ctx)
        => JsonError(ctx, StatusCodes.Status400BadRequest, "unauthorized_client", DeviceGrantRefusal);

    /// <summary>The description of every <c>unauthorized_client</c> refusal of the device grant (SEC-0024).</summary>
    internal const string DeviceGrantRefusal = "This client is not allowed the Device Authorization Grant.";

    // Reject absurd ids (a 1MB user_code / rid) before any DB access or allocation. Valid ids are <= 64 chars.
    private static async Task<bool> RejectOversizedId(HttpContext ctx, string? id)
    {
        if ((id?.Length ?? 0) <= 128) return false;
        await JsonError(ctx, StatusCodes.Status404NotFound, "invalid_request", "Unknown or expired request.");
        return true;
    }

    internal static string? Subject(ClaimsPrincipal user)
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
        => ctx.Response.Cookies.Delete(BindingCookiePrefix + rid, new CookieOptions { Path = AppPaths.BindingCookie(ctx) });

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
