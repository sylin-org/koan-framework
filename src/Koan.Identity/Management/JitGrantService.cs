using Koan.Data.Core;
using Koan.Web.Authorization;

namespace Koan.Identity.Management;

/// <summary>
/// SEC-0007 Layer 3 — just-in-time, time-boxed grants over the SEC-0005 <see cref="AgentGrant"/> primitive: no
/// standing admin. A JIT grant always expires; it can be extended pre-expiry (one-click); customer-grantable
/// read-only support access is a grant like any other (distinct from impersonation). Revocation is fresh-per-request
/// (the gate re-reads grants), so <c>Remove()</c> / expiry take effect on the very next call. Grants are
/// tenant-scoped (per SEC-0005) — call inside the relevant tenant scope.
/// </summary>
public sealed class JitGrantService
{
    public async Task<AgentGrant> GrantAsync(string subject, string capability, string resource, TimeSpan ttl, CancellationToken ct = default)
    {
        var grant = new AgentGrant
        {
            Subject = subject,
            Capability = capability,
            Resource = resource,
            ExpiresAt = DateTimeOffset.UtcNow.Add(ttl),
        };
        return await grant.Save(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Customer-grantable, time-boxed support access — a JIT grant of the <c>support:read</c> capability (distinct
    /// from impersonation). NOTE: "read-only" is realized by the app binding <c>support:read</c> to read actions in
    /// its <c>[Access]</c> / <c>EntityAccess&lt;T&gt;</c> rules; this method confers the capability, it does not by
    /// itself restrict the grantee to reads.
    /// </summary>
    public Task<AgentGrant> GrantSupportAccessAsync(string supportSubject, string resource, TimeSpan ttl, CancellationToken ct = default)
        => GrantAsync(supportSubject, "support:read", resource, ttl, ct);

    /// <summary>Extend a grant before it expires (the one-click extend). Returns null if the grant is gone.</summary>
    public async Task<AgentGrant?> ExtendAsync(string grantId, TimeSpan extension, CancellationToken ct = default)
    {
        var grant = await AgentGrant.Get(grantId, ct).ConfigureAwait(false);
        if (grant is null) return null;
        var basis = grant.ExpiresAt is { } e && e > DateTimeOffset.UtcNow ? e : DateTimeOffset.UtcNow;
        grant.ExpiresAt = basis.Add(extension);
        return await grant.Save(ct).ConfigureAwait(false);
    }

    public async Task<bool> RevokeAsync(string grantId, CancellationToken ct = default)
    {
        var grant = await AgentGrant.Get(grantId, ct).ConfigureAwait(false);
        if (grant is null) return false;
        await grant.Remove(ct).ConfigureAwait(false);
        return true;
    }
}
