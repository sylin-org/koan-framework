using Microsoft.AspNetCore.Mvc;
using Sora.Web.Auth.Infrastructure;

namespace Sora.Web.Auth.Controllers;

[ApiController]
public sealed class SamlController : ControllerBase
{
    [HttpGet(AuthConstants.Routes.SamlMetadata)]
    public IActionResult Metadata([FromRoute] string provider)
    {
        return Problem(detail: $"SAML metadata not implemented for provider '{provider}'.", statusCode: 501);
    }

    [HttpPost(AuthConstants.Routes.SamlAcs)]
    public IActionResult Acs([FromRoute] string provider)
    {
        return Problem(detail: $"SAML ACS not implemented for provider '{provider}'.", statusCode: 501);
    }
}
