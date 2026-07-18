using Koan.Data.Core;
using Koan.Identity.Tenancy.Deprovisioning;
using SnapVault.Models;

namespace SnapVault.Services;

/// <summary>
/// Close one client's gallery access. Composes tenant-seat removal with the SnapVault domain purge and emits an
/// integrity-checked operation record. The guest's PhotoAsset reads fail closed when the grants are gone; the record
/// describes completed writes and is not an external erasure attestation.
/// </summary>
public sealed class SnapVaultDeprovisioningService
{
    private readonly DeprovisioningService _platform;
    public SnapVaultDeprovisioningService(DeprovisioningService platform) => _platform = platform;

    public async Task<ClientAccessClosureReceipt> CloseAccessAsync(string guestId, string studioTenantId, CancellationToken ct = default)
    {
        // 1. Domain purge — the guest's access fail-closes the instant the grants are gone.
        var grants = await GalleryGrant.Query(g => g.IdentityId == guestId && g.StudioTenantId == studioTenantId, ct);
        var selections = await ProofSelection.Query(p => p.GuestIdentityId == guestId && p.StudioTenantId == studioTenantId, ct);
        var grantsRemoved = 0;
        var selectionsRemoved = 0;
        foreach (var x in grants) if (await x.Remove(ct)) grantsRemoved++;
        foreach (var x in selections) if (await x.Remove(ct)) selectionsRemoved++;

        // 2. Remove the studio Membership and retain the framework operation receipt.
        var seat = await _platform.RemoveFromTenantAsync(guestId, studioTenantId, ct);

        // 3. The chained, integrity-checked application operation record.
        var receipt = new ClientAccessClosureReceipt
        {
            GuestIdentityId = guestId,
            StudioTenantId = studioTenantId,
            GrantsRemoved = grantsRemoved,
            SelectionsRemoved = selectionsRemoved,
            SeatReceiptId = seat.Id,
            SeatReceiptHash = seat.Hash,
            Surfaces = { "gallery-grant", "proof-selection", "membership" },
            OccurredAt = DateTimeOffset.UtcNow,
        };
        receipt.Hash = receipt.ComputeHash();
        return await receipt.Save(ct);
    }
}
