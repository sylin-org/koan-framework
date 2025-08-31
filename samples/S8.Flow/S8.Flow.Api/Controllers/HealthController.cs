using Microsoft.AspNetCore.Mvc;
using S8.Flow.Api.Adapters;
using S8.Flow.Shared;
using Sora.Data.Core;
using Sora.Flow.Infrastructure;

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

    [HttpGet("latest/{referenceId}")]
    public async Task<IActionResult> GetLatest(string referenceId, CancellationToken ct = default)
    {
        using (DataSetContext.With(FlowSets.ViewShort(S8.Flow.Api.Hosting.LatestReadingProjector.ViewName)))
        {
            var id = $"{S8.Flow.Api.Hosting.LatestReadingProjector.ViewName}::{referenceId}";
            var doc = await Data<SensorLatestReading, string>.GetAsync(id, ct);
            if (doc is null) return NotFound();
            return Ok(new { referenceId, view = doc.View });
        }
    }
}
