using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Koan.Web.Auth.Extensions;

namespace Koan.Web.Auth.Integration.Tests;

/// <summary>
/// The resource edge's assertion surface: a cookie sign-in standing in for the application's login, an anonymous page
/// reporting who the request is and which scheme authenticated it, and a page that requires an authenticated caller.
/// </summary>
[ApiController]
public sealed class ResourceEdgeController : ControllerBase
{
    [HttpGet("/edge/signin")]
    public async Task<IActionResult> SignIn()
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "alice"), new Claim(ClaimTypes.Name, "Alice")],
            AuthenticationExtensions.CookieScheme);
        await HttpContext.SignInAsync(AuthenticationExtensions.CookieScheme, new ClaimsPrincipal(identity));
        return Ok(new { ok = true });
    }

    [HttpGet("/edge/whoami")]
    public IActionResult WhoAmI() => Ok(new
    {
        authenticated = User.Identity?.IsAuthenticated == true,
        id = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
        scheme = HttpContext.Features.Get<IAuthenticateResultFeature>()?.AuthenticateResult?.Ticket?.AuthenticationScheme,
    });

    [Authorize]
    [HttpGet("/edge/protected")]
    public IActionResult Protected() => Ok(new { id = User.FindFirst(ClaimTypes.NameIdentifier)?.Value });
}
