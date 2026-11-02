using System.Security.Cryptography;
using Koan.Data.Core;
using Koan.Web.Auth.Server.Infrastructure;
using Koan.Web.Auth.Server.Options;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Koan.Web.Auth.Server.Protocol;

/// <summary>
/// SEC-0028 — what an application's own consent and done pages need from the authorization server, in process, so a
/// page rendered on the server reads Koan's answer instead of Koan's entities. <see cref="Find"/> looks a pending request
/// up by its <c>rid</c> or by the user code a person typed (normalised, and counted against the same per-address
/// allowance as Koan's <c>/oauth/request/{id}</c>), and says where Allow and Deny post. <see cref="Outcome"/> reads what
/// the person decided from the done page's address, in a form only this server can write.
/// </summary>
public static class ConsentPage
{
    /// <summary>The query parameter that carries the protected outcome to the done page.</summary>
    public const string OutcomeParameter = "outcome";

    private const string OutcomePurpose = "Koan.Web.Auth.Server.ConsentOutcome.v1";
    private const char OutcomeSeparator = '\n';
    private const int MaximumIdLength = 128;
    private const int MaximumOutcomeLength = 2048;
    private const string UserCodeAllowance = "usercode:";
    private const string UnknownAddress = "unknown";

    /// <summary>
    /// The request waiting under <paramref name="id"/>: a code-flow <c>rid</c>, or a Device-flow user code in any case,
    /// with or without its hyphen or spaces. A user-code lookup counts against the address's
    /// <c>UserCodeVerificationRateLimitPerMinute</c>, shared with Koan's own request endpoint and its approve and deny;
    /// past it the answer is <see cref="ConsentLookup.Throttled"/>. A device request whose client is not allowed the
    /// Device Authorization Grant is answered <see cref="ConsentLookup.ClientNotAllowed"/>, without the request.
    /// </summary>
    public static async Task<ConsentLookup> Find(HttpContext context, string? id, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrWhiteSpace(id) || id.Length > MaximumIdLength) return new(null, Throttled: false);
        var services = context.RequestServices;
        var options = services.GetRequiredService<IOptions<AuthServerOptions>>().Value;
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow();

        var consent = await ConsentRequest.Get(id, ct);
        if (consent is not null && consent.IsPending && !consent.IsExpired(now))
        {
            var pending = await Pending(context, consent.Id, consent.ClientId, consent.Scope, consent.Resource,
                userCode: null, consent.RedirectUri, ct);
            return new(pending, Throttled: false);
        }

        if (!TryAcquireUserCode(context, options, now)) return new(null, Throttled: true);
        var device = await FindPendingDevice(id, now, ct);
        if (device is null) return new(null, Throttled: false);
        // SEC-0024: the client's permission is read again here, so a request made before it lost the device grant never
        // puts its name in front of the person.
        if (!await OAuthClient.Allows(device.ClientId, OAuthGrantTypes.DeviceCode, now, ct))
            return new(null, Throttled: false) { ClientNotAllowed = true };
        return new(await Pending(context, device.UserCode, device.ClientId, device.Scope, device.Resource,
            UserCode.Format(device.UserCode), redirectUri: null, ct), Throttled: false);
    }

    /// <summary>
    /// What the person decided, read from the done page's <see cref="OutcomeParameter"/>; null when the address carries
    /// none, or one this server did not write, or one that has expired, or one recorded for a person other than the
    /// one signed in by this request's session. A plain or altered value never reads as an approval or a denial.
    /// </summary>
    public static async Task<ConsentOutcome?> Outcome(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var value = context.Request.Query[OutcomeParameter].ToString();
        if (value.Length == 0 || value.Length > MaximumOutcomeLength) return null;

        string payload;
        try { payload = Protector(context).Unprotect(value, out _); }
        catch (CryptographicException) { return null; }

        var parts = payload.Split(OutcomeSeparator);
        if (parts.Length != 2) return null;
        ConsentOutcome? outcome = parts[0] switch
        {
            nameof(ConsentOutcome.Approved) => ConsentOutcome.Approved,
            nameof(ConsentOutcome.Denied) => ConsentOutcome.Denied,
            _ => null,
        };
        if (outcome is null || parts[1].Length == 0) return null;

        var session = await OAuthProtocolEndpoints.Session(context);
        return session is not null && string.Equals(OAuthProtocolEndpoints.Subject(session), parts[1], StringComparison.Ordinal)
            ? outcome
            : null;
    }

    /// <summary>The done page's address carrying <paramref name="outcome"/>, protected and bound to the deciding
    /// <paramref name="subject"/>, for as long as a consent request lives.</summary>
    internal static string DoneUrl(HttpContext context, AuthServerOptions options, ConsentOutcome outcome, string subject)
    {
        var token = Protector(context).Protect(outcome + OutcomeSeparator.ToString() + subject, options.ConsentRequestLifetime);
        return QueryHelpers.AddQueryString(RequestHost.Url(context, AppPaths.Done(context, options)), OutcomeParameter, token);
    }

    /// <summary>D8 — one user-code attempt against the address's per-minute allowance; false when it is spent.</summary>
    internal static bool TryAcquireUserCode(HttpContext context, AuthServerOptions options, DateTimeOffset now)
    {
        var limiter = context.RequestServices.GetRequiredService<FixedWindowRateLimiter>();
        var address = context.Connection.RemoteIpAddress?.ToString() ?? UnknownAddress;
        return limiter.TryAcquire(UserCodeAllowance + address, options.UserCodeVerificationRateLimitPerMinute, TimeSpan.FromMinutes(1), now);
    }

    /// <summary>The pending, unexpired device request a typed user code names, normalised as it is stored.</summary>
    internal static async Task<DeviceCode?> FindPendingDevice(string userCodeInput, DateTimeOffset now, CancellationToken ct)
    {
        var normalized = UserCode.Normalize(userCodeInput);
        if (normalized.Length == 0) return null;
        var matches = await DeviceCode.Query(d => d.UserCode == normalized, ct);
        return matches.FirstOrDefault(d => d.IsPending && !d.IsExpired(now));
    }

    private static async Task<PendingConsent> Pending(HttpContext context, string id, string clientId, string scope,
        string resource, string? userCode, string? redirectUri, CancellationToken ct)
    {
        var client = await OAuthClient.Get(clientId, ct);
        // The page shows what approving would give: for a person signed in, what they hold of what was requested (D6).
        IReadOnlyList<string> scopes = await OAuthProtocolEndpoints.Session(context) is { } person
            ? OAuthProtocolEndpoints.Granted(scope, person)
            : OAuthProtocolEndpoints.Requested(scope);
        return new PendingConsent(
            id,
            clientId,
            client?.ClientName is { Length: > 0 } name ? name : clientId,
            scopes,
            resource,
            userCode,
            redirectUri,
            AppPaths.Request(context, AuthServerRoutes.Approve, id),
            AppPaths.Request(context, AuthServerRoutes.Deny, id));
    }

    private static ITimeLimitedDataProtector Protector(HttpContext context)
        => context.RequestServices.GetRequiredService<IDataProtectionProvider>().CreateProtector(OutcomePurpose).ToTimeLimitedDataProtector();
}
