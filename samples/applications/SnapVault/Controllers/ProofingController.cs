using Koan.Data.Access;
using Koan.Data.Core;
using Koan.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SnapVault.Initialization;
using SnapVault.Models;
using SnapVault.Services;

namespace SnapVault.Controllers;

/// <summary>
/// The invited-guest proofing surface (SnapVault step 5e) + the studio's client-selections read. The GUEST-WRITE
/// FLOOR is the security-critical piece: a mark is authorized against the guest's own <see cref="GalleryGrant"/>,
/// and every scope id (event, studio) is derived SERVER-SIDE from the ambient subject + the photo — never from the
/// caller. The photo is loaded under the guest's constrained subject, so a cross-set photo id resolves to null (the
/// SEC-0008 access axis) and is refused before any write. This is the "granted-but-not-enforced" defect the §9.7
/// tripwire named: the grant's permissions and the read scope must BOTH gate the write, not just the read.
/// </summary>
[ApiController]
[Route("api/proofing")]
public sealed class ProofingController : ControllerBase
{
    private readonly ProofingService _proofing;

    public ProofingController(ProofingService proofing) => _proofing = proofing;

    /// <summary>Guest: set my mark on a photo I can see. All ids are derived server-side; the grant + read scope gate it.</summary>
    [HttpPost("{photoId}")]
    public async Task<IActionResult> SetMark(string photoId, [FromBody] ProofMarkRequest request, CancellationToken ct = default)
    {
        // Must be a CONSTRAINED guest subject (an operator is unconstrained; a job is system) — proofing is guest-only.
        var subject = Subject.Current;
        if (subject is null || subject.IsSystem || !subject.IsConstrained || string.IsNullOrEmpty(subject.Id))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Proofing requires an invited-guest session." });
        var guestId = subject.Id!;

        if (request is null || (request.Favorite is null && request.Rating is null && request.Selected is null && request.Comment is null))
            return BadRequest(new { error = "No mark provided." });

        // Load the photo UNDER the guest's subject: a cross-set (or cross-studio) photo id resolves to null → refuse.
        var photo = await PhotoAsset.Get(photoId, ct);
        if (photo is null) return NotFound();

        // Derive the grant from (guest, photo.EventId) — never a caller-supplied event/studio id.
        var grant = await GalleryGrant.Get(GalleryGrant.KeyFor(guestId, photo.EventId));
        if (grant is null || !grant.IsActive) return StatusCode(StatusCodes.Status403Forbidden, new { error = "No active grant for this photo." });

        // Permission floor: a favorite/rating/select mark needs "select"; a comment needs "comment".
        var wantsMark = request.Favorite is not null || request.Rating is not null || request.Selected is not null;
        if (wantsMark && !grant.Allows("select"))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "This gallery does not allow selecting." });
        if (request.Comment is not null && !grant.Allows("comment"))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "This gallery does not allow comments." });

        var rating = request.Rating is null ? (int?)null : Math.Clamp(request.Rating.Value, 0, 5);   // clamp — never trust the caller
        var sel = await _proofing.SetSelectionAsync(
            guestId, photo.EventId, photoId, grant.StudioTenantId,
            favorite: request.Favorite, rating: rating, selected: request.Selected, comment: request.Comment, ct: ct);

        return Ok(new { sel.PhotoId, sel.IsFavorite, sel.Rating, sel.IsSelected, sel.Comment });
    }

    /// <summary>Studio: the client's selected picks for one event (operator read; the studio is the ambient tenant).</summary>
    [HttpGet("selections/{eventId}")]
    [OperatorOnly]   // studio-wide data (every guest's picks) — never a constrained guest
    public async Task<IActionResult> Selections(string eventId, CancellationToken ct = default)
    {
        var studioTenantId = Tenant.Current?.Id;
        if (string.IsNullOrEmpty(studioTenantId)) return NotFound();
        var selections = await _proofing.SelectionsForEventAsync(eventId, studioTenantId!, ct);
        return Ok(selections.Select(s => new { s.PhotoId, s.GuestIdentityId, s.IsFavorite, s.Rating, s.IsSelected, s.Comment }));
    }
}
