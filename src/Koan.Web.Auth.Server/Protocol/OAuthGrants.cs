using System.Security.Cryptography;
using System.Text;
using Koan.Data.Core;
using Koan.Web.Authorization;

namespace Koan.Web.Auth.Server.Protocol;

/// <summary>
/// SEC-0006 D9 — the revocable grant behind "authorize once". An approval creates (or reuses) a SEC-0005
/// <see cref="AgentGrant"/> keyed by <c>(subject, client, scope-set)</c>; refresh tokens reference it and a live
/// grant is required to refresh — so revoking the grant (the user, or an admin, fleet-wide) fails the next
/// refresh closed and re-prompts consent. The grant's <c>Resource</c> is the OAuth resource URI (not an entity
/// name or <c>*</c>), so it is inert at the SEC-0004 entity gates and serves purely as the OAuth session record.
/// </summary>
internal static class OAuthGrants
{
    private const string CapabilityPrefix = "mcp:grant:";

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

    public static async Task<AgentGrant> FindOrCreateAsync(string subject, string clientId,
        IReadOnlyCollection<string> scopes, string resource, DateTimeOffset now, TimeSpan lifetime, CancellationToken ct)
    {
        var existing = await FindLiveAsync(subject, clientId, scopes, now, ct);
        if (existing is not null) return existing;
        var grant = new AgentGrant
        {
            Subject = subject,
            Capability = Capability(clientId, scopes),
            Resource = resource,
            ExpiresAt = now + lifetime,
        };
        await grant.Save(ct);
        return grant;
    }

    public static async Task<AgentGrant?> FindLiveAsync(string subject, string clientId,
        IReadOnlyCollection<string> scopes, DateTimeOffset now, CancellationToken ct)
    {
        var cap = Capability(clientId, scopes);
        var matches = await AgentGrant.Query(g => g.Subject == subject && g.Capability == cap, ct);
        return matches.FirstOrDefault(g => g.IsActive(now));
    }

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
