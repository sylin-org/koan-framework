using Koan.Tenancy;
using Microsoft.AspNetCore.Mvc;
using SnapVault.Initialization;
using SnapVault.Models;
using SnapVault.Services;

namespace SnapVault.Controllers;

/// <summary>
/// Operator-owned studio-to-client gallery access. A grant targets a known durable person and one event; the resulting
/// membership permits tenant entry while the gallery grant constrains photo access to that event.
/// </summary>
[ApiController]
[Route("api/gallery")]
public sealed class GalleryController : ControllerBase
{
    private readonly GalleryGrantService _grants;

    public GalleryController(GalleryGrantService grants) => _grants = grants;

    /// <summary>Studio: grant a known durable person access to one event.</summary>
    [HttpPost("grant")]
    [OperatorOnly]
    public async Task<IActionResult> Grant([FromBody] GalleryGrantRequest request, CancellationToken ct = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.EventId) || string.IsNullOrWhiteSpace(request.IdentityId))
            return BadRequest(new { error = "eventId and identityId are required." });

        var studioTenantId = Tenant.Current?.Id;
        if (string.IsNullOrEmpty(studioTenantId))
            return BadRequest(new { error = "No studio tenant resolved for the grant." });

        var role = string.IsNullOrWhiteSpace(request.Role) ? GalleryGrant.Template.Proofer : request.Role!;
        var result = await _grants.GrantAsync(studioTenantId!, request.EventId, request.IdentityId, role, ct);
        if (result.Grant is null)
            return BadRequest(new { outcome = result.Outcome.ToString(), error = "Gallery access could not be granted." });
        return Ok(new { outcome = result.Outcome.ToString(), eventId = result.Grant.EventId });
    }
}
