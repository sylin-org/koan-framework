using Microsoft.AspNetCore.Mvc;
using Sora.Flow.Monitoring;
using Sora.Messaging;

namespace Sora.Flow.Web.Controllers;

[ApiController]
[Route("api/flow/adapters")]
public sealed class FlowAdaptersController : ControllerBase
{
    private readonly IAdapterRegistry _registry;
    private readonly IMessageBus _bus;
    public FlowAdaptersController(IAdapterRegistry registry, IMessageBus bus)
    {
        _registry = registry;
        _bus = bus;
    }

    [HttpGet]
    public IActionResult GetAll()
    {
        var items = _registry.All();
        var aggregate = items
            .GroupBy(x => (x.System, x.Adapter))
            .ToDictionary(g => $"{g.Key.System}:{g.Key.Adapter}", g => g.Count(), StringComparer.OrdinalIgnoreCase);
        return Ok(new { total = items.Count, by = aggregate, items });
    }

    [HttpPost("seed")]
    public async Task<IActionResult> SeedAll([FromQuery] int? count = null, CancellationToken ct = default)
    {
        var adapters = _registry.All();
        if (adapters.Count == 0)
            return NotFound(new { error = "No adapters registered" });

        var tasks = adapters.Select(adapter =>
            _bus.SendAsync(new Sora.Flow.Model.ControlCommand
            {
                Verb = "seed",
                Target = $"{adapter.System}:{adapter.Adapter}",
                Parameters = count.HasValue
                    ? new System.Collections.Generic.Dictionary<string, System.Text.Json.JsonElement>(System.StringComparer.OrdinalIgnoreCase)
                        { ["count"] = System.Text.Json.JsonSerializer.SerializeToElement(count.Value) }
                    : new System.Collections.Generic.Dictionary<string, System.Text.Json.JsonElement>(System.StringComparer.OrdinalIgnoreCase)
            }, ct)
        ).ToArray();
        await Task.WhenAll(tasks);
        return Accepted(new { status = "seed command dispatched", count = adapters.Count });
    }
}
