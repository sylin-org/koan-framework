using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Koan.Data.Core;
using Koan.Identity.Management;

namespace Koan.Identity.Web;

/// <summary>
/// SEC-0007 Layer 1 — the end-user self-service console. Every action is scoped to the calling principal's subject
/// (the security boundary): profile, verified emails, connected accounts, sessions &amp; devices + "sign out
/// everywhere-else", and personal access tokens (issue/rotate/revoke; ownership-checked).
/// </summary>
[ApiController]
[Authorize]
[Route("api/identity/me")]
public sealed class IdentitySelfServiceController : ControllerBase
{
    private readonly SessionService _sessions;
    private readonly ApiTokenService _tokens;

    public IdentitySelfServiceController(SessionService sessions, ApiTokenService tokens)
    {
        _sessions = sessions;
        _tokens = tokens;
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
        => Subject is null ? Unauthorized() : Ok(await ExternalIdentityLink.Query(l => l.IdentityId == Subject, ct));

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

    [HttpGet("tokens")]
    public async Task<ActionResult<IEnumerable<object>>> Tokens(CancellationToken ct)
    {
        if (Subject is null) return Unauthorized();
        var tokens = await _tokens.ListAsync(Subject, ct);
        return Ok(tokens.Select(Project));
    }

    public sealed record IssueTokenRequest(string Name, List<string>? Scopes, DateTimeOffset? ExpiresAt);

    [HttpPost("tokens")]
    public async Task<ActionResult<object>> IssueToken([FromBody] IssueTokenRequest req, CancellationToken ct)
    {
        if (Subject is null) return Unauthorized();
        var issued = await _tokens.IssueAsync(Subject, req.Name, req.Scopes ?? new(), req.ExpiresAt, ct);
        return Ok(new { token = Project(issued.Token), secret = issued.Secret });
    }

    [HttpPost("tokens/{id}/rotate")]
    public async Task<ActionResult<object>> RotateToken([FromRoute] string id, CancellationToken ct)
    {
        if (Subject is null) return Unauthorized();
        var existing = await ApiToken.Get(id, ct);
        if (existing is null || existing.IdentityId != Subject) return NotFound(); // never rotate another person's token
        var rotated = await _tokens.RotateAsync(id, ct);
        return rotated is null ? NotFound() : Ok(new { token = Project(rotated.Token), secret = rotated.Secret });
    }

    [HttpDelete("tokens/{id}")]
    public async Task<IActionResult> RevokeToken([FromRoute] string id, CancellationToken ct)
    {
        if (Subject is null) return Unauthorized();
        var existing = await ApiToken.Get(id, ct);
        if (existing is null || existing.IdentityId != Subject) return NotFound();
        await _tokens.RevokeAsync(id, ct);
        return NoContent();
    }

    // Project the token WITHOUT the secret hash — the secret itself is only ever returned once, at issue/rotate.
    private static object Project(ApiToken t)
        => new { t.Id, t.Name, t.Scopes, t.ExpiresAt, t.LastUsedAt, t.Revoked, t.CreatedAt, t.RotatedFromId };
}
