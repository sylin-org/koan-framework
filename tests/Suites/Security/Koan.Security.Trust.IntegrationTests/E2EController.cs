using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Koan.Security.Trust.Inbound;

namespace Koan.Security.Trust.IntegrationTests;

/// <summary>
/// SEC-0001/SEC-0002 end-to-end surface: open, identity echo, bearer-protected, and bare-[Authorize] endpoints,
/// exercised over the real HTTP pipeline (auth schemes, dev identity, authorization).
/// </summary>
[ApiController]
public sealed class E2EController : ControllerBase
{
    [HttpGet("/e2e/open")]
    public IActionResult Open() => Ok(new { ok = true });

    [HttpGet("/e2e/whoami")]
    public IActionResult WhoAmI() => Ok(new
    {
        authenticated = Identity.Current.IsAuthenticated,
        id = Identity.Current.Id,
        roles = Identity.Current.Roles,
    });

    // Opt-in bearer scheme: requires a valid KSVID; the dev identity does not satisfy it.
    [Authorize(AuthenticationSchemes = KoanBearerDefaults.AuthenticationScheme)]
    [HttpGet("/e2e/bearer")]
    public IActionResult BearerProtected() => Ok(new { sub = User.FindFirst("sub")?.Value });

    // Bare [Authorize]: cookie/default scheme — satisfied by the zero-config dev identity in Development.
    [Authorize]
    [HttpGet("/e2e/cookie")]
    public IActionResult CookieProtected() => Ok(new { id = Identity.Current.Id });
}
