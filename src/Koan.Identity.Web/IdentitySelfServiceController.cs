using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Koan.Data.Core;
using Koan.Identity.Management;

namespace Koan.Identity.Web;

/// <summary>
/// SEC-0007 Layer 1 — the end-user self-service API. Every action is scoped to the calling principal's subject
/// (the security boundary): profile, verified emails, connected accounts, sessions &amp; devices + "sign out
/// everywhere-else".
/// </summary>
[ApiController]
[Authorize]
[Route("api/identity/me")]
public sealed class IdentitySelfServiceController : ControllerBase
{
    private readonly SessionService _sessions;
    private readonly IdentityLinkService _links;

    public IdentitySelfServiceController(SessionService sessions, IdentityLinkService links)
    {
        _sessions = sessions;
        _links = links;
    }

    private string? Subject => User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;

    [HttpGet]
    public async Task<ActionResult<Identity>> Profile(CancellationToken ct)
    {
        if (Subject is null) return Unauthorized();
        var me = await Identity.Get(Subject, ct);
        return me is null ? NotFound() : Ok(me);
    }

    [HttpGet("emails")]
    public async Task<ActionResult<IReadOnlyList<IdentityEmail>>> Emails(CancellationToken ct)
        => Subject is null ? Unauthorized() : Ok(await IdentityEmail.Query(e => e.IdentityId == Subject, ct));

    [HttpGet("connected")]
    public async Task<ActionResult<IReadOnlyList<ExternalIdentityLink>>> Connected(CancellationToken ct)
        => Subject is null ? Unauthorized() : Ok(await _links.ForPersonAsync(Subject, ct));

    // Unlink a connected account (unlink-safe across all factor types — you can only unlink your OWN links).
    // Linking a NEW provider happens via that provider's auth callback (so the sub is verified), not a raw POST.
    [HttpDelete("connected/{linkId}")]
    public async Task<IActionResult> Unlink([FromRoute] string linkId, CancellationToken ct)
    {
        if (Subject is null) return Unauthorized();
        return await _links.UnlinkAsync(Subject, linkId, ct) ? NoContent() : NotFound();
    }

    [HttpGet("sessions")]
    public async Task<ActionResult<IReadOnlyList<Session>>> Sessions(CancellationToken ct)
        => Subject is null ? Unauthorized() : Ok(await _sessions.ListAsync(Subject, ct));

    public sealed record SignOutOthersRequest(string CurrentSessionId);

    [HttpPost("sessions/sign-out-others")]
    public async Task<ActionResult<object>> SignOutOthers([FromBody] SignOutOthersRequest req, CancellationToken ct)
    {
        if (Subject is null) return Unauthorized();
        var revoked = await _sessions.SignOutEverywhereElseAsync(Subject, req.CurrentSessionId, ct);
        return Ok(new { revoked });
    }

}
