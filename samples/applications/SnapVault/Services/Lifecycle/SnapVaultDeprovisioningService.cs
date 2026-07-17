using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Data.Core;
using Koan.Identity;
using Koan.Identity.Tenancy.Deprovisioning;
using Koan.Tenancy;
using SnapVault.Models;

namespace SnapVault.Services;

/// <summary>
/// "Delete this client &amp; prove it." Composes the shipped atomic seat removal (<see cref="DeprovisioningService"/> —
/// removes the studio <c>Membership</c>, emits a verifiable receipt) with the SnapVault domain purge (all the client's
/// <see cref="GalleryGrant"/>s + <see cref="ProofSelection"/>s in the studio) and emits a chained
/// <see cref="ClientErasureCertificate"/>. The guest's PhotoAsset reads fail closed the instant the grants are gone
/// through the access axis: request-path enforcement plus proof, never a write-only flag.
/// </summary>
public sealed class SnapVaultDeprovisioningService
{
    private readonly DeprovisioningService _platform;
    public SnapVaultDeprovisioningService(DeprovisioningService platform) => _platform = platform;

    public async Task<ClientErasureCertificate> RevokeClientAsync(string guestId, string studioTenantId, CancellationToken ct = default)
    {
        // 1. Domain purge — the guest's access fail-closes the instant the grants are gone.
        var grants = await GalleryGrant.Query(g => g.IdentityId == guestId && g.StudioTenantId == studioTenantId, ct);
        var selections = await ProofSelection.Query(p => p.GuestIdentityId == guestId && p.StudioTenantId == studioTenantId, ct);
        var grantsRemoved = 0;
        var selectionsRemoved = 0;
        foreach (var x in grants) if (await x.Remove(ct)) grantsRemoved++;
        foreach (var x in selections) if (await x.Remove(ct)) selectionsRemoved++;

        // 1b. Revoke any still-PENDING invites addressed to this client's VERIFIED emails in the studio — otherwise an
        //     un-accepted invite survives fully redeemable, letting the "erased" client re-accept and regain access
        //     (and the certificate would assert a closure that isn't enforced). Consumed invites are already inert
        //     (Status=Accepted ⇒ not redeemable), so only pending ones need revoking.
        var emails = (await IdentityEmail.Query(e => e.IdentityId == guestId && e.Verified, ct))
            .Select(e => e.Address).ToHashSet(StringComparer.Ordinal);
        var invitesRevoked = 0;
        if (emails.Count > 0)
        {
            var pending = await Invite.Query(i => i.TenantId == studioTenantId && i.Status == InviteStatus.Pending, ct);
            foreach (var inv in pending.Where(i => emails.Contains(i.Email)))
            {
                var gi = await GalleryInvite.Get(GalleryInvite.KeyFor(inv.Id));
                if (gi is not null) await gi.Remove(ct);
                if (await inv.Remove(ct)) invitesRevoked++;
            }
        }

        // 2. Shipped atomic seat removal + its verifiable receipt (removes the studio Membership).
        var seat = await _platform.RemoveFromTenantAsync(guestId, studioTenantId, ct);

        // 3. The chained, tamper-evident client erasure certificate.
        var cert = new ClientErasureCertificate
        {
            GuestIdentityId = guestId,
            StudioTenantId = studioTenantId,
            GrantsRemoved = grantsRemoved,
            SelectionsRemoved = selectionsRemoved,
            InvitesRevoked = invitesRevoked,
            SeatReceiptId = seat.Id,
            SeatReceiptHash = seat.Hash,
            Surfaces = { "gallery-grant", "proof-selection", "invite", "membership" },
            OccurredAt = DateTimeOffset.UtcNow,
        };
        cert.Hash = cert.ComputeHash();
        return await cert.Save(ct);
    }
}
