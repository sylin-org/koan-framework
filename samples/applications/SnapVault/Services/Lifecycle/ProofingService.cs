using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Koan.Data.Core;
using SnapVault.Models;

namespace SnapVault.Services;

/// <summary>
/// The proofing gallery: an invited guest marks photos (favorite / rating / "I select this" / comment), attributed to
/// the GUEST via <see cref="ProofSelection"/> — deliberately separate from the studio's own
/// <c>PhotoAsset.IsFavorite</c>/<c>Rating</c> — and the studio reads the client's selections back. Whether the guest
/// can see the photo at all is enforced upstream by the access axis; this service only records the marks.
/// </summary>
public sealed class ProofingService
{
    /// <summary>Set/update the guest's mark on one photo (deterministic per (guest, photo) — idempotent upsert).</summary>
    public async Task<ProofSelection> SetSelectionAsync(
        string guestId, string eventId, string photoId, string studioTenantId,
        bool? favorite = null, int? rating = null, bool? selected = null, string? comment = null,
        CancellationToken ct = default)
    {
        var sel = await ProofSelection.Get(ProofSelection.KeyFor(guestId, photoId)) ?? new ProofSelection
        {
            Id = ProofSelection.KeyFor(guestId, photoId),
        };
        // Re-anchor the scope fields from the (controller-derived, trusted) values on EVERY write — an existing row
        // is never allowed to keep a stale/mismatched guest, event, or studio (the caller already proved the guest
        // can see the photo + holds the grant these came from).
        sel.GuestIdentityId = guestId;
        sel.EventId = eventId;
        sel.PhotoId = photoId;
        sel.StudioTenantId = studioTenantId;
        if (favorite is not null) sel.IsFavorite = favorite.Value;
        if (rating is not null) sel.Rating = rating.Value;
        if (selected is not null) sel.IsSelected = selected.Value;
        if (comment is not null) sel.Comment = comment;
        return await sel.Save(ct);
    }

    /// <summary>The client's selected picks for an event — the studio's "client selections" view.</summary>
    public async Task<IReadOnlyList<ProofSelection>> SelectionsForEventAsync(
        string eventId, string studioTenantId, CancellationToken ct = default)
        => await ProofSelection.Query(p => p.EventId == eventId && p.StudioTenantId == studioTenantId && p.IsSelected, ct);
}
