using Microsoft.AspNetCore.Mvc;
using Koan.Web.Auth.Domain;
using Koan.Web.Auth.Infrastructure;
using System.Security.Claims;

namespace Koan.Web.Auth.Controllers;

[ApiController]
public sealed class MeController(IExternalIdentityStore identities, ICurrentUserProjector projector) : ControllerBase
{
    [HttpGet(AuthConstants.Routes.Me)]
    public async Task<ActionResult<CurrentUserDto>> GetMe(CancellationToken ct)
    {
        // Handle authentication check manually to avoid exceptions for unauthenticated users
        if (!User.Identity?.IsAuthenticated == true)
            return Unauthorized();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized();

        var links = await identities.GetByUser(userId, ct);
        var dto = await projector.Project(User, links, ct);
        return Ok(dto);
    }
}
