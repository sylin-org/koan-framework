using System.Security.Cryptography;
using System.Text;
using Koan.Core.Hosting.App;
using Koan.Data.Core;
using Koan.Data.Core.Lifecycle;
using Koan.Web.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Web.Auth.Server.Protocol;

/// <summary>
/// SEC-0006 D9 — the revocable grant behind "authorize once". Each approval issues its own SEC-0005
/// <see cref="AgentGrant"/>: every code redemption and device-code exchange starts a refresh family, and the family's
/// tokens reference that one grant, so revoking it (the person, or an admin, fleet-wide) fails that family's next
/// refresh closed and leaves the person's other approvals alone. The grant's <c>Resource</c> is the OAuth resource URI
/// (not an entity name or <c>*</c>), so it is inert at the SEC-0004 entity gates and serves purely as the OAuth session
/// record. Remembered consent is a <see cref="RememberedConsent"/> keyed by the person, the grant's capability term
/// (client and scope set) and the resource: the person's approval records it, and removing any live grant for that key
/// forgets it, so a revoked approval re-prompts while the person's other approvals keep refreshing. The grant records
/// when it was issued and the client it was issued to, with the name that client registered (SEC-0005), so a consumer
/// never reads them from the capability term. With refresh tokens off, nothing would reference a grant, so none is
/// issued and no consent is remembered.
/// </summary>
internal static class OAuthGrants
{
    private const string CapabilityPrefix = "mcp:grant:";

    /// <summary>How long forgetting a revoked grant's consent may take, independent of the caller's token.</summary>
    private static readonly TimeSpan ForgetConsentBound = TimeSpan.FromSeconds(10);

    public static string Capability(string clientId, IEnumerable<string> scopes)
        => CapabilityPrefix + clientId + ":" + ScopeHash(scopes);

    public static string ScopeHash(IEnumerable<string> scopes)
    {
        var normalized = string.Join(' ', scopes
            .Select(s => s.Trim()).Where(s => s.Length > 0)
            .Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal));
        return OpaqueToken.Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    /// <summary>The stored row id for a raw refresh token — its hash (the raw value is never persisted).</summary>
    public static string HashToken(string raw)
        => Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(raw))).ToLowerInvariant();

    /// <summary>
    /// Issue the grant behind one approval: a new refresh family's revocable unit. When <paramref name="remember"/>, the
    /// person consented on the consent page, and their consent is remembered for as long as this grant lives.
    /// </summary>
    public static async Task<AgentGrant> IssueAsync(string subject, string clientId, IReadOnlyCollection<string> scopes,
        string resource, DateTimeOffset now, TimeSpan lifetime, bool remember, CancellationToken ct)
    {
        var client = await OAuthClient.Get(clientId, ct);
        var grant = new AgentGrant
        {
            Subject = subject,
            Capability = Capability(clientId, scopes),
            Resource = resource,
            ExpiresAt = now + lifetime,
            IssuedAt = now,
            ClientId = clientId,
            ClientName = client?.ClientName is { Length: > 0 } name ? name : null,
        };
        await grant.Save(ct);
        if (remember)
            await new RememberedConsent { Id = ConsentKey(subject, grant.Capability, resource), ExpiresUtc = grant.ExpiresAt.Value }.Save(ct);
        return grant;
    }

    /// <summary>Whether the person's consent to this client, scope set and resource is remembered at <paramref name="now"/>.</summary>
    public static async Task<bool> RemembersAsync(string subject, string clientId,
        IReadOnlyCollection<string> scopes, string resource, DateTimeOffset now, CancellationToken ct)
        => await RememberedConsent.Get(ConsentKey(subject, Capability(clientId, scopes), resource), ct) is { } consent
            && consent.IsLive(now);

    /// <summary>
    /// SEC-0005 — the <see cref="AgentGrant"/> before-remove hook: removing a live grant this server issued first forgets
    /// the person's remembered consent for its key. Every removal path runs it — <c>Remove()</c>, a removal by id, the
    /// access explainer's revoke, erasure, and refresh-token reuse. It runs before the delete and on a bounded token of
    /// its own, not the caller's, so a revoke fails toward asking the person again: if the caller's token is cancelled or
    /// the delete then fails, the grant may survive but its consent is already gone; if forgetting fails, the removal
    /// stops and both survive for a retry. Removing an expired grant (the sweep) forgets nothing, since it revokes nothing.
    /// </summary>
    public static async ValueTask<EntityLifecycleResult> ForgetConsent(EntityLifecycleContext<AgentGrant> context)
    {
        var grant = context.Current;
        if (!grant.Capability.StartsWith(CapabilityPrefix, StringComparison.Ordinal)) return context.Proceed();
        var now = AppHost.Current?.GetService<TimeProvider>()?.GetUtcNow() ?? DateTimeOffset.UtcNow;
        if (!grant.IsActive(now)) return context.Proceed();
        using var bound = new CancellationTokenSource(ForgetConsentBound);
        await RememberedConsent.Remove(ConsentKey(grant.Subject, grant.Capability, grant.Resource), bound.Token);
        return context.Proceed();
    }

    /// <summary>The <see cref="RememberedConsent"/> id: a hash, so the row names no person in the clear.</summary>
    internal static string ConsentKey(string subject, string capability, string resource)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', subject, capability, resource)))).ToLowerInvariant();

    public static async Task<bool> GrantLiveAsync(string grantId, DateTimeOffset now, CancellationToken ct)
    {
        var g = string.IsNullOrEmpty(grantId) ? null : await AgentGrant.Get(grantId, ct);
        return g is not null && g.IsActive(now);
    }

    public static async Task RevokeGrantAsync(string grantId, CancellationToken ct)
    {
        var g = string.IsNullOrEmpty(grantId) ? null : await AgentGrant.Get(grantId, ct);
        if (g is not null) await g.Remove(ct);
    }

    public static async Task RevokeFamilyAsync(string familyId, CancellationToken ct)
    {
        foreach (var t in await RefreshToken.Query(t => t.FamilyId == familyId, ct))
            await t.Remove(ct);
    }
}
