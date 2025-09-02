using Microsoft.AspNetCore.Mvc;
using S8.Flow.Api.Adapters;

namespace S8.Flow.Api.Controllers;

[ApiController]
[Route("adapters")] // /adapters/health
public sealed class HealthController : ControllerBase
{
    private readonly IAdapterHealthRegistry _reg;
    public HealthController(IAdapterHealthRegistry reg) { _reg = reg; }

    [HttpGet("health")] // returns map of adapter name -> health
    public IActionResult GetHealth()
        => Ok(_reg.Snapshot());
}
