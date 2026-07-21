using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace Koan.Web.Auth.Integration.Tests;

/// <summary>Minimal endpoint that reports the current principal — the post-round-trip auth assertion surface.</summary>
[ApiController]
public sealed class WhoAmIController : ControllerBase
{
    [HttpGet("/e2e/whoami")]
    public IActionResult WhoAmI()
    {
        var u = HttpContext.User;
        return Ok(new
        {
            authenticated = u.Identity?.IsAuthenticated ?? false,
            id = u.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            name = u.FindFirst(ClaimTypes.Name)?.Value,
            roles = u.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray(),
            permissions = u.FindAll("Koan.permission").Select(c => c.Value).ToArray()
        });
    }
}
