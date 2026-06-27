using Koan.Data.Core;
using Koan.Identity;
using Koan.Identity.Management;
using Koan.Tenancy;

namespace Koan.Identity.Tenancy.Deprovisioning;

/// <summary>
/// SEC-0007 P4 — atomic verifiable deprovisioning. "deactivated = cannot act" is ENFORCED on the request path, never
/// a write-only flag: a full <see cref="DeactivateAsync"/> sets <c>Identity.Status = Deactivated</c> (so
/// <c>SessionGuard</c> fail-closes every cookie at the next validation tick) and revokes all sessions now; a
/// <see cref="RemoveFromTenantAsync"/> deletes the tenant <c>Membership</c>, so the tenant-resolution middleware can
/// no longer scope the person in and the fail-closed axis seals their tenant data. Each emits a verifiable
/// <see cref="DeprovisioningReceipt"/> as the durable proof.
/// <para>The revocation <c>Epoch</c> bump is <b>recorded</b> for SEC-0001's future bearer-token revocation, but is
/// NOT yet read on any request path (mint-epoch-into-tokens + validate-against-current is a deferred Trust change,
/// SEC-0001 Phase 3). The live closure for a deactivated person is <c>Status</c> + session revocation (cookie path);
/// an already-issued bearer token remains valid until its natural expiry — the receipt's <c>Surfaces</c> deliberately
/// do not list "tokens".</para>
/// </summary>
public sealed class DeprovisioningService
{
    private readonly SessionService _sessions;

    public DeprovisioningService(SessionService sessions) => _sessions = sessions;

    /// <summary>Deactivate the whole person — they can no longer act anywhere. Idempotent (a re-run reports StatusSet=null).</summary>
    public async Task<DeprovisioningReceipt> DeactivateAsync(string identityId, CancellationToken ct = default)
    {
        var person = await Identity.Get(identityId, ct).ConfigureAwait(false);
        string? statusSet = null;
        if (person is not null && person.Status != IdentityStatus.Deactivated)
        {
            person.Status = IdentityStatus.Deactivated;
            person.Epoch++; // SEC-0001 revocation epoch — RECORDED for a future bearer-token check; not yet read on any
                            // request path (deferred to SEC-0001 Phase 3). Live closure = Status + the session revoke below.
            await person.Save(ct).ConfigureAwait(false);
            statusSet = nameof(IdentityStatus.Deactivated);
        }

        var revoked = await _sessions.RevokeAllAsync(identityId, ct).ConfigureAwait(false);

        return await WriteReceiptAsync(new DeprovisioningReceipt
        {
            IdentityId = identityId,
            TenantId = null,
            Kind = DeprovisioningKind.Deactivation,
            SessionsRevoked = revoked,
            MembershipsRemoved = 0,
            StatusSet = statusSet,
            Surfaces = { "data", "storage", "cache", "sessions" },
        }, ct).ConfigureAwait(false);
    }

    /// <summary>Remove a person's seat in one tenant — they lose that tenant's access; the person and other seats persist. Idempotent.</summary>
    public async Task<DeprovisioningReceipt> RemoveFromTenantAsync(string identityId, string tenantId, CancellationToken ct = default)
    {
        var memberships = await Membership.Query(m => m.IdentityId == identityId && m.TenantId == tenantId, ct).ConfigureAwait(false);
        var removed = 0;
        foreach (var m in memberships)
            if (await m.Remove(ct).ConfigureAwait(false)) removed++; // count rows actually deleted, not merely found

        return await WriteReceiptAsync(new DeprovisioningReceipt
        {
            IdentityId = identityId,
            TenantId = tenantId,
            Kind = DeprovisioningKind.SeatRemoval,
            SessionsRevoked = 0,
            MembershipsRemoved = removed,
            StatusSet = null,
            // Tenant-scoped surfaces are sealed by the fail-closed axis the moment the membership is gone (the
            // middleware stops scoping the person into this tenant). The person's sessions are NOT revoked — they may
            // hold other seats.
            Surfaces = { "data", "storage", "cache" },
        }, ct).ConfigureAwait(false);
    }

    private static async Task<DeprovisioningReceipt> WriteReceiptAsync(DeprovisioningReceipt receipt, CancellationToken ct)
    {
        receipt.OccurredAt = DateTimeOffset.UtcNow;
        receipt.Hash = receipt.ComputeHash();
        return await receipt.Save(ct).ConfigureAwait(false);
    }
}
