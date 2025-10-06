using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;

namespace Koan.Service.Inbox.Connector.Redis.Controllers;

[ApiController]
[Route("health")]
[Produces("application/json")]
public sealed class HealthController : ControllerBase
{
    private readonly IConnectionMultiplexer _connectionMultiplexer;

    public HealthController(IConnectionMultiplexer connectionMultiplexer)
        => _connectionMultiplexer = connectionMultiplexer ?? throw new ArgumentNullException(nameof(connectionMultiplexer));

    [HttpGet("live")]
    public IActionResult Live() => Ok(new { status = "live" });

    [HttpGet("ready")]
    public async Task<IActionResult> Ready()
    {
        try { var _ = await _connectionMultiplexer.GetDatabase().PingAsync(); return Ok(new { status = "ready" }); }
        catch { return StatusCode(503); }
    }
}

