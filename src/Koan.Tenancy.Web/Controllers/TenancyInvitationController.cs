using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Koan.Identity.Tenancy.Invitations;
using Koan.Tenancy.Web.Authorization;

namespace Koan.Tenancy.Web.Controllers;

public sealed record IssueInvitationRequest(string TenantId, string Email, string Role, DateTimeOffset? ExpiresAt);
public sealed record IssuedInvitationResponse(string InviteId, string Token, DateTimeOffset ExpiresAt);
public sealed record AcceptInvitationRequest(string Token);
public sealed record AcceptInvitationResponse(string Outcome, string? TenantId, string? Role);

/// <summary>
/// The supported invitation ceremony over HTTP (PMC-035): operators issue and revoke under the
/// tenancy-operator policy; the invited person — any authenticated subject — accepts their own token.
/// The raw token appears exactly once, in the issue response, and is never stored server-side.
/// </summary>
[Route("api/tenancy/invitations")]
public sealed class TenancyInvitationAcceptanceController(
    InviteAcceptanceService acceptance) : ControllerBase
{
    /// <summary>Accept one invitation as the signed-in person. Outcomes are reported, never thrown:
    /// the body names what happened (including losing a race) so a browser can say why.</summary>
    [HttpPost("accept")]
    [Authorize]
    public async Task<IActionResult> Accept([FromBody] AcceptInvitationRequest request, CancellationToken ct)
    {
        var identityId = User?.FindFirst("sub")?.Value
                         ?? User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(identityId)) return Unauthorized();

        var result = await acceptance.AcceptAsync(request.Token, identityId, ct).ConfigureAwait(false);
        return result.Outcome switch
        {
            InviteAcceptOutcome.Accepted => Ok(new AcceptInvitationResponse(
                result.Outcome.ToString(), result.Membership?.TenantId, result.Membership?.Roles.FirstOrDefault())),
            _ => BadRequest(new AcceptInvitationResponse(result.Outcome.ToString(), null, null)),
        };
    }
}

[Route("api/tenancy/invitations")]
[Authorize(Policy = TenancyWebPolicies.Operator)]
public sealed class TenancyInvitationAdminController(
    InviteIssuanceService issuance) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<IssuedInvitationResponse>> Issue(
        [FromBody] IssueInvitationRequest request, CancellationToken ct)
    {
        var issued = await issuance.IssueAsync(
            Actor(), request.TenantId, request.Email, request.Role, request.ExpiresAt, ct)
            .ConfigureAwait(false);
        return Ok(new IssuedInvitationResponse(issued.Invite.Id, issued.Token, issued.Invite.ExpiresAt));
    }

    [HttpPost("{id}/revoke")]
    public async Task<IActionResult> Revoke([FromRoute] string id, CancellationToken ct)
    {
        var revoked = await issuance.RevokeAsync(Actor(), id, ct).ConfigureAwait(false);
        return revoked ? NoContent() : NotFound();
    }

    private string Actor()
        => User?.FindFirst("sub")?.Value
           ?? User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
           ?? User?.Identity?.Name
           ?? "operator (unattributed)";
}
