using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Koan.Security.Trust.Inbound;

namespace Koan.Web.Auth.Server.IntegrationTests;

/// <summary>
/// Test-only resource surface: an endpoint that only <c>Koan.bearer</c> admits, echoing the token's subject and issuer,
/// so a spec can ask the bearer scheme itself whether it accepts a token; and an application action that admits
/// anonymous callers.
/// </summary>
[ApiController]
public sealed class TestIdentityController : ControllerBase
{
    [Authorize(AuthenticationSchemes = KoanBearerDefaults.AuthenticationScheme)]
    [HttpGet("/test/bearer")]
    public IActionResult Bearer() => Ok(new { sub = User.FindFirst("sub")?.Value, iss = User.FindFirst("iss")?.Value });

    [HttpGet("/test/anonymous")]
    public IActionResult Anonymous() => Ok(new { authenticated = User.Identity?.IsAuthenticated == true });
}
