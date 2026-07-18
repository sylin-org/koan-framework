using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Koan.Data.Core;
using Koan.Identity.Impersonation;
using Koan.Identity.Management;

namespace Koan.Identity.Web;

/// <summary>
/// SEC-0007 Layer 1 — the operator management API. Gated on the operator role (the SEC-0004 capability floor constrains
/// further). User list / search, bulk suspend &amp; reactivate (suspend ≠ delete), and lifecycle-aware delete.
/// </summary>
[ApiController]
[Authorize(Roles = IdentityRoles.Operator)]
[Route("api/identity/admin")]
public sealed class IdentityAdminController : ControllerBase
{
    private readonly IdentityLifecycleService _lifecycle;

    public IdentityAdminController(IdentityLifecycleService lifecycle) => _lifecycle = lifecycle;

    [HttpGet("identities")]
    public async Task<ActionResult<IReadOnlyList<Identity>>> List([FromQuery] string? q, [FromQuery] int size = 50, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(q))
            return Ok(await Identity.FirstPage(size, ct));

        var matches = (await Identity.All(ct))
            .Where(i => (i.DisplayName ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                        || i.Id.Contains(q, StringComparison.OrdinalIgnoreCase))
            .Take(size)
            .ToList();
        return Ok(matches);
    }

    [HttpGet("identities/{id}")]
    public async Task<ActionResult<Identity>> Get([FromRoute] string id, CancellationToken ct)
    {
        var person = await Identity.Get(id, ct);
        return person is null ? NotFound() : Ok(person);
    }

    public sealed record BulkRequest(List<string>? IdentityIds);

    [HttpPost("identities/suspend")]
    public async Task<ActionResult<IdentityLifecycleService.BulkResult>> Suspend([FromBody] BulkRequest req, CancellationToken ct)
    {
        if (ImpersonationGuard.IsBlocked(User, "identity.suspend")) return StatusCode(403, new { error = "suspending identities is blocked while impersonating" });
        return Ok(await _lifecycle.SuspendAsync(req.IdentityIds ?? new(), ct));
    }

    [HttpPost("identities/reactivate")]
    public async Task<ActionResult<IdentityLifecycleService.BulkResult>> Reactivate([FromBody] BulkRequest req, CancellationToken ct)
        => Ok(await _lifecycle.ReactivateAsync(req.IdentityIds ?? new(), ct));

    [HttpDelete("identities/{id}")]
    public async Task<ActionResult<IdentityLifecycleService.DeleteReport>> Delete([FromRoute] string id, CancellationToken ct)
    {
        if (ImpersonationGuard.IsBlocked(User, "identity.delete")) return StatusCode(403, new { error = "deleting identities is blocked while impersonating" });
        return Ok(await _lifecycle.DeleteWithDependentsAsync(id, ct));
    }
}
