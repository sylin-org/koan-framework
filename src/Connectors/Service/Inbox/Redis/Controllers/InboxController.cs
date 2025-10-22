using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

using Koan.Service.Inbox.Connector.Redis.Options;

namespace Koan.Service.Inbox.Connector.Redis.Controllers;

[ApiController]
[Route("v1/inbox")]
[Produces("application/json")]
public sealed class InboxController : ControllerBase
{
    private readonly IConnectionMultiplexer _connectionMultiplexer;
    private readonly RedisInboxOptions _options;

    public InboxController(IConnectionMultiplexer connectionMultiplexer, IOptions<RedisInboxOptions> optionsAccessor)
    {
        _connectionMultiplexer = connectionMultiplexer ?? throw new ArgumentNullException(nameof(connectionMultiplexer));
        _options = optionsAccessor?.Value ?? throw new ArgumentNullException(nameof(optionsAccessor));
    }

    public sealed record MarkRequest(string Key);

    // GET /v1/inbox/{key}
    [HttpGet("{key}")]
    public async Task<IActionResult> GetStatus([FromRoute] string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return BadRequest();
        var db = _connectionMultiplexer.GetDatabase();
        var val = await db.StringGetAsync(BuildKey(key));
        if (val.IsNullOrEmpty) return NotFound();
        return Ok(new { status = "Processed" });
    }

    // POST /v1/inbox/mark-processed
    [HttpPost("mark-processed")]
    public async Task<IActionResult> MarkProcessed([FromBody] MarkRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Key)) return BadRequest();
        var db = _connectionMultiplexer.GetDatabase();
        await db.StringSetAsync(BuildKey(req.Key), "1", _options.ProcessingTtl);
        return Ok(new { status = "Processed" });
    }

    private string BuildKey(string key)
        => string.Concat(_options.KeyPrefix, key);
}

