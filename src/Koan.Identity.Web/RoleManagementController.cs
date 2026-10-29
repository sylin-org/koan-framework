using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using Koan.Identity.Impersonation;
using Koan.Identity.Roles;

namespace Koan.Identity.Web;

/// <summary>Bounded server-role management and current-person bag projection.</summary>
[ApiController]
[Authorize]
[ServiceFilter(typeof(RoleManagementExceptionFilter))]
[Route("api/identity/roles")]
public sealed class RoleManagementController(RoleCollection roles, IOptions<RoleOptions> options) : ControllerBase
{
    [HttpGet("descriptor")]
    public ActionResult<object> Descriptor() => Ok(new
    {
        tokens = new { everyone = RoleTokens.Everyone, authenticated = RoleTokens.Authenticated,
            rolePrefix = RoleTokens.RolePrefix, groupPrefix = RoleTokens.GroupPrefix, globalPrefix = RoleTokens.GlobalPrefix,
            administrator = RoleTokens.Administrator },
        limits = options.Value,
    });

    [HttpGet("me/bag")]
    public async Task<ActionResult<RoleBag>> MyBag(CancellationToken ct)
    {
        var person = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(person)) return Forbid();
        return Ok(await roles.Bag(person, true, ct));
    }

    [HttpGet]
    [Authorize(Roles = IdentityRoles.Operator)]
    public async Task<ActionResult<RolePage>> Page([FromQuery] string? search = null, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        if (page < 1 || pageSize < 1 || pageSize > options.Value.MaxPageSize) return BadRequest();
        return Ok(await roles.Page(search, page, pageSize, ct));
    }

    [HttpGet("{key}")]
    [Authorize(Roles = IdentityRoles.Operator)]
    public async Task<ActionResult<Role>> Get(string key, CancellationToken ct)
        => await roles.Get(key, ct) is { } role ? Ok(role) : NotFound();

    public sealed record SaveRoleRequest(string Name, IReadOnlyList<string> Permissions,
        IReadOnlyDictionary<string, string>? Metadata = null);

    [HttpPut("{key}")]
    [Authorize(Roles = IdentityRoles.Operator)]
    public async Task<ActionResult<Role>> Put(string key, [FromBody] SaveRoleRequest request, CancellationToken ct)
        => Impersonating() ?? (ActionResult<Role>)Ok(await roles.Define(key, request.Name, request.Permissions, request.Metadata, ct));

    [HttpPatch("{key}")]
    [Authorize(Roles = IdentityRoles.Operator)]
    public async Task<ActionResult<Role>> Patch(string key, [FromBody] SaveRoleRequest request, CancellationToken ct)
    {
        if (Impersonating() is { } refused) return refused;
        try { return Ok(await roles.Update(key, request.Name, request.Permissions, request.Metadata, ct)); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [HttpDelete("{key}")]
    [Authorize(Roles = IdentityRoles.Operator)]
    public async Task<IActionResult> Delete(string key, CancellationToken ct)
        => Impersonating() ?? (await roles.Delete(key, ct) ? NoContent() : (IActionResult)NotFound());

    [HttpGet("{key}/members")]
    [Authorize(Roles = IdentityRoles.Operator)]
    public async Task<ActionResult<object>> Members(string key, [FromQuery] int page = 1, [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        if (page < 1 || pageSize < 1 || pageSize > options.Value.MaxPageSize) return BadRequest();
        var role = await roles.Get(key, ct); if (role is null) return NotFound();
        return Ok(new { items = role.Members.Skip((page - 1) * pageSize).Take(pageSize), page, pageSize });
    }

    [HttpPut("{key}/members/{personId}")]
    [Authorize(Roles = IdentityRoles.Operator)]
    public async Task<ActionResult<Role>> AddMember(string key, string personId, CancellationToken ct)
    {
        if (Impersonating() is { } refused) return refused;
        try { return Ok(await roles.Add(key, personId, ct)); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (RoleBoundException bound) { return Conflict(new { error = bound.Message }); }
    }

    [HttpDelete("{key}/members/{personId}")]
    [Authorize(Roles = IdentityRoles.Operator)]
    public async Task<ActionResult<Role>> RemoveMember(string key, string personId, CancellationToken ct)
    {
        if (Impersonating() is { } refused) return refused;
        try { return Ok(await roles.Remove(key, personId, ct)); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    /// <summary>SEC-0007 D8: a role write is refused while the caller impersonates.</summary>
    private ObjectResult? Impersonating() => ImpersonationGuard.IsBlocked(User, ImpersonationGuard.RoleManage)
        ? StatusCode(StatusCodes.Status403Forbidden, new { error = "changing roles is blocked while impersonating" })
        : null;
}

internal sealed class RoleManagementExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        var status = context.Exception switch
        {
            ArgumentException => StatusCodes.Status400BadRequest,
            UnauthorizedAccessException => StatusCodes.Status403Forbidden,
            _ => 0,
        };
        if (status == 0) return;
        context.Result = new ObjectResult(new { error = context.Exception.Message }) { StatusCode = status };
        context.ExceptionHandled = true;
    }
}
