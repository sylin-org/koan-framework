using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sora.Web.Auth.Domain;
using Sora.Web.Auth.Infrastructure;

namespace Sora.Web.Auth.Controllers;

[ApiController]
public sealed class MeController(IExternalIdentityStore identities) : ControllerBase
{
    [Authorize]
    [HttpGet(AuthConstants.Routes.Me)]
    public async Task<ActionResult<CurrentUserDto>> GetMe(CancellationToken ct)
    {
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
