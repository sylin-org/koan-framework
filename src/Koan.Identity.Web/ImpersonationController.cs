using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Koan.Identity.Access;
using Koan.Identity.Impersonation;

namespace Koan.Identity.Web;

/// <summary>
/// SEC-0007 D8 / Layer 3 — the operator impersonation API: request (reason+ticket) → approve (a DIFFERENT
/// approver, time-boxed) → revoke, plus the "who can act as <c>{target}</c>" view. The structural safety (no
/// God-mode) lives in <see cref="ImpersonationGuard"/> + the actor-claim model, not here.
/// </summary>
[ApiController]
[Authorize(Roles = IdentityRoles.Operator)]
[Route("api/identity/admin/impersonation")]
public sealed class ImpersonationController : ControllerBase
{
    private readonly ImpersonationService _impersonation;
    private readonly EffectiveAccessResolver _resolver;

    public ImpersonationController(ImpersonationService impersonation, EffectiveAccessResolver resolver)
    {
        _impersonation = impersonation;
        _resolver = resolver;
    }

    private string? Actor => User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;

    public sealed record RequestBody(string Target, string Reason, string? Ticket);

    [HttpPost("request")]
    public async Task<ActionResult<ImpersonationGrant>> RequestImpersonation([FromBody] RequestBody body, CancellationToken ct)
    {
        if (Actor is null) return Unauthorized();
        if (ImpersonationGuard.IsBlocked(User, "impersonate.start")) return StatusCode(403, new { error = "nested impersonation is blocked" });
        try { return Ok(await _impersonation.RequestAsync(Actor, body.Target, body.Reason, body.Ticket, ct)); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { return BadRequest(new { error = ex.Message }); }
    }

    public sealed record ApproveBody(int TtlMinutes);

    [HttpPost("{grantId}/approve")]
    public async Task<ActionResult<ImpersonationGrant>> Approve([FromRoute] string grantId, [FromBody] ApproveBody body, CancellationToken ct)
    {
        if (Actor is null) return Unauthorized();
        var ttl = TimeSpan.FromMinutes(Math.Clamp(body.TtlMinutes, 1, 480));
        try
        {
            var grant = await _impersonation.ApproveAsync(grantId, Actor, ttl, ct);
            return grant is null ? NotFound() : Ok(grant);
        }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); } // no self-approval
    }

    [HttpDelete("{grantId}")]
    public async Task<IActionResult> Revoke([FromRoute] string grantId, CancellationToken ct)
        => await _impersonation.RevokeAsync(grantId, ct) ? NoContent() : NotFound();

    [HttpGet("for/{target}")]
    public async Task<ActionResult<IReadOnlyList<ImpersonationGrant>>> ForTarget([FromRoute] string target, CancellationToken ct)
        => Ok(await _impersonation.ForTargetAsync(target, ct));

    /// <summary>Start an impersonation session — issues a cookie with sub=target + the actor claim (fail-closed on no active grant).</summary>
    [HttpPost("{grantId}/start")]
    public async Task<IActionResult> Start([FromRoute] string grantId, CancellationToken ct)
    {
        if (Actor is null) return Unauthorized();
        var grant = await ImpersonationGrant.Get(grantId, ct);
        if (grant is null || grant.Actor != Actor) return NotFound();

        var access = await _resolver.ResolveAsync(grant.Target, ct);
        var principal = await _impersonation.BuildSessionAsync(Actor, grant.Target, access.Roles, ct);
        if (principal is null) return StatusCode(403, new { error = "no active approved grant to impersonate this target" });

        await HttpContext.SignInAsync(principal); // the impersonated cookie carries sub=target + koan_actor=operator
        return Ok(new { impersonating = grant.Target });
    }

    /// <summary>Stop impersonating — restores the operator's own session.</summary>
    [HttpPost("stop")]
    public async Task<IActionResult> Stop(CancellationToken ct)
    {
        var actor = ImpersonationClaims.ActorOf(User);
        if (actor is null) return Ok(new { impersonating = (string?)null });

        var access = await _resolver.ResolveAsync(actor, ct);
        var identity = new ClaimsIdentity("cookie");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, actor));
        foreach (var role in access.Roles) identity.AddClaim(new Claim(ClaimTypes.Role, role));
        await HttpContext.SignInAsync(new ClaimsPrincipal(identity));
        return Ok(new { stopped = true });
    }
}
