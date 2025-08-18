using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;

namespace Sora.Service.Inbox.Redis.Controllers;

[ApiController]
[Route("health")]
[Produces("application/json")]
public sealed class HealthController : ControllerBase
{
    [HttpGet("live")]
    public IActionResult Live() => Ok(new { status = "live" });

    [HttpGet("ready")]
    public async Task<IActionResult> Ready([FromServices] IConnectionMultiplexer cm)
    {
        try { var _ = await cm.GetDatabase().PingAsync(); return Ok(new { status = "ready" }); }
        catch { return StatusCode(503); }
    }
}
