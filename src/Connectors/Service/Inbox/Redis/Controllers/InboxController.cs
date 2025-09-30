using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;

namespace Koan.Service.Inbox.Connector.Redis.Controllers;

[ApiController]
[Route("v1/inbox")]
[Produces("application/json")]
public sealed class InboxController : ControllerBase
{
    public sealed record MarkRequest(string Key);

    // GET /v1/inbox/{key}
    [HttpGet("{key}")]
    public async Task<IActionResult> GetStatus([FromRoute] string key, [FromServices] IConnectionMultiplexer cm)
    {
        if (string.IsNullOrWhiteSpace(key)) return BadRequest();
        var db = cm.GetDatabase();
        var val = await db.StringGetAsync($"inbox:{key}");
        if (val.IsNullOrEmpty) return NotFound();
        return Ok(new { status = "Processed" });
    }

    // POST /v1/inbox/mark-processed
    [HttpPost("mark-processed")]
    public async Task<IActionResult> MarkProcessed([FromBody] MarkRequest req, [FromServices] IConnectionMultiplexer cm)
    {
        if (string.IsNullOrWhiteSpace(req.Key)) return BadRequest();
        var db = cm.GetDatabase();
        await db.StringSetAsync($"inbox:{req.Key}", "1", TimeSpan.FromHours(24));
        return Ok(new { status = "Processed" });
    }
}

