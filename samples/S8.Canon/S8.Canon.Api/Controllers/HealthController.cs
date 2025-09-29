using Microsoft.AspNetCore.Mvc;
using S8.Canon.Api.Adapters;

namespace S8.Canon.Api.Controllers;

[ApiController]
[Route("api/adapters")] // normalized API prefix
public sealed class HealthController : ControllerBase
{
    private readonly IAdapterHealthRegistry _reg;
    public HealthController(IAdapterHealthRegistry reg) { _reg = reg; }

    [HttpGet("health")] // returns map of adapter name -> health
    public IActionResult GetHealth()
        => Ok(_reg.Snapshot());
}
