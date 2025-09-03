using Microsoft.AspNetCore.Mvc;
using Sora.Flow.Monitoring;

namespace Sora.Flow.Web.Controllers;

[ApiController]
[Route("api/flow/adapters")]
public sealed class FlowAdaptersController : ControllerBase
{
    private readonly IAdapterRegistry _registry;
    public FlowAdaptersController(IAdapterRegistry registry) => _registry = registry;

    [HttpGet]
    public IActionResult GetAll()
    {
        var items = _registry.All();
        var aggregate = items
            .GroupBy(x => (x.System, x.Adapter))
            .ToDictionary(g => $"{g.Key.System}:{g.Key.Adapter}", g => g.Count(), StringComparer.OrdinalIgnoreCase);
        return Ok(new { total = items.Count, by = aggregate, items });
    }
}
