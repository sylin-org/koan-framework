using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Koan.Identity.Access;
using Koan.Identity.Management;

namespace Koan.Identity.Web;

/// <summary>
/// SEC-0007 Layer 2 — the operator access console: the effective-access view ("X can do A,B,C"), the bidirectional
/// explainer (forward "can X do Y on Z?" via the real engine; reverse "why does X have access to Z?" → the exact
/// contributing rows), and global role grant/revoke.
/// </summary>
[ApiController]
[Authorize(Roles = IdentityWebRoles.Operator)]
[Route("api/identity/admin/identities/{id}/access")]
public sealed class IdentityAccessController : ControllerBase
{
    private readonly EffectiveAccessResolver _resolver;
    private readonly AccessExplainer _explainer;
    private readonly IdentityRoleService _roles;

    public IdentityAccessController(EffectiveAccessResolver resolver, AccessExplainer explainer, IdentityRoleService roles)
    {
        _resolver = resolver;
        _explainer = explainer;
        _roles = roles;
    }

    [HttpGet]
    public async Task<ActionResult<EffectiveAccess>> Effective([FromRoute] string id, CancellationToken ct)
        => Ok(await _resolver.ResolveAsync(id, ct));

    [HttpGet("why")]
    public async Task<ActionResult<IReadOnlyList<AccessFact>>> Why([FromRoute] string id, [FromQuery] string resource, CancellationToken ct)
        => Ok(await _explainer.WhyAsync(id, resource, ct));

    [HttpGet("can")]
    public async Task<ActionResult<AccessDecision>> Can([FromRoute] string id, [FromQuery] string action, [FromQuery] string? resource, CancellationToken ct)
        => Ok(await _explainer.CanAsync(id, action, resource, ct));

    public sealed record GrantRequest(string RoleKey);

    [HttpPost("roles")]
    public async Task<ActionResult<IdentityRole>> Grant([FromRoute] string id, [FromBody] GrantRequest req, CancellationToken ct)
        => Ok(await _roles.GrantAsync(id, req.RoleKey, ct));

    [HttpDelete("roles/{roleKey}")]
    public async Task<IActionResult> Revoke([FromRoute] string id, [FromRoute] string roleKey, CancellationToken ct)
        => await _roles.RevokeAsync(id, roleKey, ct) ? NoContent() : NotFound();
}
