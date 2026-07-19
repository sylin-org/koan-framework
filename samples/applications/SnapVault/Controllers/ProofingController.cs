using Koan.Data.Core;
using Koan.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SnapVault.Initialization;
using SnapVault.Models;
using SnapVault.Services;

namespace SnapVault.Controllers;

/// <summary>
/// Invited-client proofing and the studio's client-selection read. A mark is authorized against the client's own <see cref="GalleryGrant"/>,
/// and every scope id (event, studio) is derived SERVER-SIDE from the validated request context + the photo — never from the
/// caller. The photo is loaded under the validated gallery request filter, so a cross-set photo id resolves to null
/// and is refused before any write. Grant permissions and read scope both gate the write.
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
        var grant = SnapVaultContextContributor.CurrentGrant(HttpContext);
        if (grant is null)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Proofing requires an invited-guest session." });
        var guestId = grant.IdentityId;

        if (request is null || (request.Favorite is null && request.Rating is null && request.Selected is null && request.Comment is null))
            return BadRequest(new { error = "No mark provided." });

        // The request contributor already scoped PhotoAsset reads: a cross-set (or cross-studio) id resolves to null.
        var photo = await PhotoAsset.Get(photoId, ct);
        if (photo is null) return NotFound();

        if (!string.Equals(grant.EventId, photo.EventId, StringComparison.Ordinal)) return NotFound();

        // Permission floor: a favorite/rating/select mark needs "select"; a comment needs "comment".
        var wantsMark = request.Favorite is not null || request.Rating is not null || request.Selected is not null;
        if (wantsMark && !grant.Allows(GalleryGrant.Permission.Select))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "This gallery does not allow selecting." });
        if (request.Comment is not null && !grant.Allows(GalleryGrant.Permission.Comment))
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
