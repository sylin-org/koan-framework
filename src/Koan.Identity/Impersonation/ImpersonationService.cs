using System.Security.Claims;
using Koan.Data.Core;

namespace Koan.Identity.Impersonation;

/// <summary>
/// SEC-0007 D8 / Layer 3 — the impersonation lifecycle: request (reason+ticket) → approve (a DIFFERENT approver,
/// time-boxed) → revoke (target/admin). All operations are entity writes, so they self-audit (Layer 1) and the
/// active grants are a queryable trail.
/// </summary>
public sealed class ImpersonationService
{
    /// <summary>Request to impersonate <paramref name="target"/>. Pending until a different approver approves.</summary>
    public async Task<ImpersonationGrant> RequestAsync(string actor, string target, string reason, string? ticket, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Impersonation requires a reason.", nameof(reason));
        if (string.Equals(actor, target, StringComparison.Ordinal)) throw new InvalidOperationException("Cannot impersonate yourself.");

        var grant = new ImpersonationGrant { Actor = actor, Target = target, Reason = reason, Ticket = ticket };
        return await grant.Save(ct).ConfigureAwait(false);
    }

    /// <summary>Approve a pending request, time-boxed. The approver must differ from the requesting actor (no self-approval).</summary>
    public async Task<ImpersonationGrant?> ApproveAsync(string grantId, string approver, TimeSpan ttl, CancellationToken ct = default)
    {
        var grant = await ImpersonationGrant.Get(grantId, ct).ConfigureAwait(false);
        if (grant is null) return null;

        // Re-assert the invariants at the approval boundary (fail-closed) — don't trust that the request-time checks
        // still hold for an entity that could have been mutated in between.
        if (grant.Revoked) throw new InvalidOperationException("Cannot approve a revoked grant.");
        if (grant.IsApproved) throw new InvalidOperationException("Grant is already approved.");
        if (string.IsNullOrWhiteSpace(grant.Reason)) throw new InvalidOperationException("Grant has no reason.");
        if (string.Equals(grant.Actor, grant.Target, StringComparison.Ordinal))
            throw new InvalidOperationException("Actor and target must differ.");
        if (string.Equals(grant.Actor, approver, StringComparison.Ordinal))
            throw new InvalidOperationException("No self-approval: the approver must differ from the requesting actor.");

        grant.ApprovedBy = approver;
        grant.ApprovedAt = DateTimeOffset.UtcNow;
        grant.ExpiresAt = DateTimeOffset.UtcNow.Add(ttl);
        return await grant.Save(ct).ConfigureAwait(false);
    }

    /// <summary>Revoke a grant (by the target or an admin). Idempotent.</summary>
    public async Task<bool> RevokeAsync(string grantId, CancellationToken ct = default)
    {
        var grant = await ImpersonationGrant.Get(grantId, ct).ConfigureAwait(false);
        if (grant is null || grant.Revoked) return false;
        grant.Revoked = true;
        await grant.Save(ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>True when <paramref name="actor"/> currently has an active, approved, unexpired grant to impersonate <paramref name="target"/>.</summary>
    public async Task<bool> IsActiveAsync(string actor, string target, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var grants = await ImpersonationGrant.Query(g => g.Actor == actor && g.Target == target, ct).ConfigureAwait(false);
        return grants.Any(g => g.IsActive(now));
    }

    /// <summary>All grants targeting <paramref name="target"/> (for the target's "who can act as me" view + revoke).</summary>
    public Task<IReadOnlyList<ImpersonationGrant>> ForTargetAsync(string target, CancellationToken ct = default)
        => ImpersonationGrant.Query(g => g.Target == target, ct);

    /// <summary>
    /// Build the impersonated principal to sign in — but only if <paramref name="actor"/> currently holds an active,
    /// approved, unexpired grant for <paramref name="target"/>. Fail-closed: returns <see langword="null"/> when
    /// there is no active grant, so the session bridge cannot mint a principal without authorization.
    /// </summary>
    public async Task<ClaimsPrincipal?> BuildSessionAsync(string actor, string target, IEnumerable<string> targetRoles, CancellationToken ct = default)
    {
        if (!await IsActiveAsync(actor, target, ct).ConfigureAwait(false)) return null;
        return new ClaimsPrincipal(ImpersonationClaims.BuildImpersonatedIdentity(target, actor, targetRoles));
    }
}
