using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Koan.Web.Auth.Domain;
using Koan.Web.Auth.Infrastructure;
using System.Security.Claims;

namespace Koan.Web.Auth.Controllers;

[ApiController]
public sealed class MeController(IExternalIdentityStore identities) : ControllerBase
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

        var displayName = User.FindFirstValue("name") ?? User.Identity?.Name;
        var picture = User.FindFirstValue("picture");
        var links = await identities.GetByUserAsync(userId, ct);

        var dto = new CurrentUserDto
        {
            Id = userId,
            DisplayName = displayName,
            PictureUrl = picture,
            Connections = links.Select(x => new ConnectionDto
            {
                Provider = x.Provider,
                DisplayName = $"{displayName} ({x.Provider})",
                KeyHash = x.ProviderKeyHash
            }).ToArray()
        };
        return Ok(dto);
    }
}
