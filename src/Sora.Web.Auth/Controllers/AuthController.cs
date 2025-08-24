using Microsoft.AspNetCore.Mvc;
using Sora.Web.Auth.Infrastructure;

namespace Sora.Web.Auth.Controllers;

[ApiController]
public sealed class AuthController : ControllerBase
{
    [HttpGet(AuthConstants.Routes.Challenge)]
    public IActionResult Challenge([FromRoute] string provider)
    {
        return Problem(detail: $"Challenge not implemented for provider '{provider}'.", statusCode: 501);
    }

    [HttpGet(AuthConstants.Routes.Callback)]
    public IActionResult Callback([FromRoute] string provider)
    {
        return Problem(detail: $"Callback not implemented for provider '{provider}'.", statusCode: 501);
    }
}
