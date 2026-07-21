using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Koan.Web.Auth.Extensions;

namespace Koan.Web.Auth.Server.IntegrationTests;

/// <summary>
/// Test-only sign-in surface: establishes a real <c>Koan.cookie</c> session for a fixed persona, so the
/// dev-token endpoint (which mints for the current cookie user) has a principal to project. Stands in for the
/// app's real interactive login.
/// </summary>
[ApiController]
public sealed class TestSignInController : ControllerBase
{
    [HttpGet("/test/signin")]
    public async Task<IActionResult> SignIn()
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "alice"),
            new Claim(ClaimTypes.Name, "Alice"),
            new Claim(ClaimTypes.Email, "alice@example.com"),
            new Claim(ClaimTypes.Role, "admin"),
            new Claim("Koan.permission", "recs:write"),
        }, AuthenticationExtensions.CookieScheme);

        await HttpContext.SignInAsync(AuthenticationExtensions.CookieScheme, new ClaimsPrincipal(identity));
        return Ok(new { ok = true });
    }
}
