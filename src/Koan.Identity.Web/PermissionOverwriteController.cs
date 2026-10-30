using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Koan.Identity.Impersonation;
using Koan.Identity.Roles;

namespace Koan.Identity.Web;

/// <summary>
/// Operator management of per-resource overwrites, beside the role routes. The resource id is opaque and travels in
/// the query, so any id an application uses fits; the subject is a role key or a person id.
/// </summary>
[ApiController]
[Authorize(Roles = IdentityRoles.Operator)]
[ServiceFilter(typeof(RoleManagementExceptionFilter))]
[Route("api/identity/overwrites")]
public sealed class PermissionOverwriteController(RoleCollection roles) : ControllerBase
{
    /// <summary>A page of the overwrites on one resource, for every subject: roles first, then people. Pass the page's
    /// <c>next</c> as <c>after</c> for the next.</summary>
    [HttpGet]
    public async Task<ActionResult<OverwritePage>> List([FromQuery] string resource, [FromQuery] string? after = null,
        [FromQuery] int take = 50, CancellationToken ct = default)
        => Ok(await roles.Overwrites(resource, after, take, ct));

    public sealed record SaveOverwriteRequest(IReadOnlyList<string>? Allow, IReadOnlyList<string>? Deny, DateTimeOffset? ExpiresAt = null);

    /// <summary>Sets the subject's overwrite on the resource. An unknown role answers 404; an impersonating caller is
    /// refused.</summary>
    [HttpPut("{subjectKind}/{subjectId}")]
    public async Task<ActionResult<PermissionOverwrite>> Put(OverwriteSubjectKind subjectKind, string subjectId,
        [FromQuery] string resource, [FromBody] SaveOverwriteRequest request, CancellationToken ct)
    {
        if (Impersonating() is { } refused) return refused;
        try { return Ok(await roles.Overwrite(resource, subjectKind, subjectId, request.Allow ?? [], request.Deny ?? [], request.ExpiresAt, ct)); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [HttpDelete("{subjectKind}/{subjectId}")]
    public async Task<IActionResult> Delete(OverwriteSubjectKind subjectKind, string subjectId, [FromQuery] string resource,
        CancellationToken ct)
        => Impersonating() ?? (await roles.RemoveOverwrite(resource, subjectKind, subjectId, ct) ? NoContent() : (IActionResult)NotFound());

    /// <summary>SEC-0007 D8: an overwrite write is refused while the caller impersonates.</summary>
    private ObjectResult? Impersonating() => ImpersonationGuard.IsBlocked(User, ImpersonationGuard.OverwriteManage)
        ? StatusCode(StatusCodes.Status403Forbidden, new { error = "changing overwrites is blocked while impersonating" })
        : null;
}
