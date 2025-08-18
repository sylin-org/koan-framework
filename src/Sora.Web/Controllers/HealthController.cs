using Microsoft.AspNetCore.Mvc;

namespace Sora.Web.Controllers;

[ApiController]
[Produces("application/json")]
[Route("api/[controller]")]
public sealed class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new { status = "ok" });
}
