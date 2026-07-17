using System.Security.Claims;
using Koan.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SnapVault.Initialization;
using SnapVault.Services;

namespace SnapVault.Controllers;

/// <summary>
/// The studio-to-client gallery lifecycle. <b>Invite</b> is operator-only; <b>Accept</b> is the
/// signed-in guest binding the invite to their identity (verified-email ownership enforced by the shipped
/// <c>InviteAcceptanceService</c>); it reads the authenticated principal directly (a not-yet-accepted guest holds no
/// grant, so the subject middleware left them subject-less — which is correct, and the accept path touches only
/// [HostScoped] control-plane rows, no [AccessScoped] read).
/// </summary>
[ApiController]
[Route("api/gallery")]
public sealed class GalleryController : ControllerBase
{
    private readonly GalleryInviteService _invites;

    public GalleryController(GalleryInviteService invites) => _invites = invites;

    /// <summary>Studio: issue a gallery invite for an event → the guest accept link.</summary>
    [HttpPost("invite")]
    [OperatorOnly]
    public async Task<IActionResult> Invite([FromBody] GalleryInviteRequest request, CancellationToken ct = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.EventId) || string.IsNullOrWhiteSpace(request.Email))
            return BadRequest(new { error = "eventId and email are required." });

        var studioTenantId = Tenant.Current?.Id;
        if (string.IsNullOrEmpty(studioTenantId))
            return BadRequest(new { error = "No studio tenant resolved for the invite." });

        var role = string.IsNullOrWhiteSpace(request.Role) ? "proofer" : request.Role!;
        var ticket = await _invites.InviteAsync(studioTenantId!, request.EventId, request.Email, role, ct: ct);
        return Ok(new { token = ticket.Token, eventId = ticket.EventId, acceptUrl = $"/guest.html?token={ticket.Token}" });
    }

    /// <summary>Guest: accept an invite (must be signed in as the invited person).</summary>
    [HttpPost("accept")]
    public async Task<IActionResult> Accept([FromBody] GalleryAcceptRequest request, CancellationToken ct = default)
    {
        var personId = ReadSubject(User);
        if (string.IsNullOrEmpty(personId))
            return StatusCode(StatusCodes.Status401Unauthorized, new { error = "Sign in to accept the invitation." });
        if (request is null || string.IsNullOrWhiteSpace(request.Token))
            return BadRequest(new { error = "token is required." });

        var result = await _invites.AcceptAsync(request.Token, personId!, ct);
        if (result.Grant is null)
            return StatusCode(StatusCodes.Status403Forbidden, new { outcome = result.Outcome.ToString(), error = "Invitation could not be accepted." });
        return Ok(new { outcome = result.Outcome.ToString(), eventId = result.Grant.EventId });
    }

    private static string? ReadSubject(ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true) return null;
        var sub = principal.FindFirst("sub")?.Value;
        if (!string.IsNullOrEmpty(sub)) return sub;
        var nameId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return string.IsNullOrEmpty(nameId) ? null : nameId;
    }
}
