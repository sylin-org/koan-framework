using Koan.Data.Core;
using Koan.Identity;
using Koan.Identity.Management;
using Koan.Tenancy;

namespace Koan.Identity.Tenancy.Deprovisioning;

/// <summary>
/// One lifecycle chokepoint for closing a person's tenant access. A full <see cref="DeactivateAsync"/> first sets
/// <c>Identity.Status = Deactivated</c>, then revokes Koan cookie sessions and removes every tenant membership. The
/// request-path bridge independently requires an active durable person and a current membership, so deactivation
/// fails closed even if a later cleanup write fails. <see cref="RemoveFromTenantAsync"/> removes only one seat.
/// <para>These are ordered, idempotent Entity writes, not a database transaction. A receipt is written only after the
/// requested workflow completes and proves only the integrity of its recorded fields. Already-issued bearer tokens
/// and non-tenant authorization remain owned by their issuer.</para>
/// </summary>
public sealed class DeprovisioningService
{
    private readonly SessionService _sessions;

    public DeprovisioningService(SessionService sessions) => _sessions = sessions;

    /// <summary>Deactivate the person, revoke Koan cookie sessions, and remove all tenant seats. Idempotent.</summary>
    public async Task<DeprovisioningReceipt> DeactivateAsync(string identityId, CancellationToken ct = default)
    {
        var person = await Identity.Get(identityId, ct).ConfigureAwait(false);
        string? statusSet = null;
        if (person is not null && person.Status != IdentityStatus.Deactivated)
        {
            person.Status = IdentityStatus.Deactivated;
            await person.Save(ct).ConfigureAwait(false);
            statusSet = nameof(IdentityStatus.Deactivated);
        }

        var revoked = await _sessions.RevokeAllAsync(identityId, ct).ConfigureAwait(false);
        var membershipsRemoved = await RemoveMembershipsAsync(identityId, tenantId: null, ct).ConfigureAwait(false);

        return await WriteReceiptAsync(new DeprovisioningReceipt
        {
            IdentityId = identityId,
            TenantId = null,
            Kind = DeprovisioningKind.Deactivation,
            SessionsRevoked = revoked,
            MembershipsRemoved = membershipsRemoved,
            StatusSet = statusSet,
            Surfaces =
            {
                DeprovisioningSurfaces.TenantData,
                DeprovisioningSurfaces.TenantStorage,
                DeprovisioningSurfaces.TenantCache,
                DeprovisioningSurfaces.CookieSessions,
            },
        }, ct).ConfigureAwait(false);
    }

    /// <summary>Remove a person's seat in one tenant — they lose that tenant's access; the person and other seats persist. Idempotent.</summary>
    public async Task<DeprovisioningReceipt> RemoveFromTenantAsync(string identityId, string tenantId, CancellationToken ct = default)
    {
        var removed = await RemoveMembershipsAsync(identityId, tenantId, ct).ConfigureAwait(false);

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
            Surfaces =
            {
                DeprovisioningSurfaces.TenantData,
                DeprovisioningSurfaces.TenantStorage,
                DeprovisioningSurfaces.TenantCache,
            },
        }, ct).ConfigureAwait(false);
    }

    private static async Task<int> RemoveMembershipsAsync(string identityId, string? tenantId, CancellationToken ct)
    {
        var memberships = tenantId is null
            ? await Membership.Query(m => m.IdentityId == identityId, ct).ConfigureAwait(false)
            : await Membership.Query(m => m.IdentityId == identityId && m.TenantId == tenantId, ct).ConfigureAwait(false);

        var removed = 0;
        foreach (var membership in memberships)
        {
            if (await membership.Remove(ct).ConfigureAwait(false)) removed++;
        }

        return removed;
    }

    private static async Task<DeprovisioningReceipt> WriteReceiptAsync(DeprovisioningReceipt receipt, CancellationToken ct)
    {
        receipt.OccurredAt = DateTimeOffset.UtcNow;
        receipt.Hash = receipt.ComputeHash();
        return await receipt.Save(ct).ConfigureAwait(false);
    }
}
