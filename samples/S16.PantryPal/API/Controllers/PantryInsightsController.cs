using Microsoft.AspNetCore.Mvc;
using S16.PantryPal.Infrastructure;
using S16.PantryPal.Services;

namespace S16.PantryPal.Controllers;

[ApiController]
[Route(PantryRoutes.InsightsBase)]
public class PantryInsightsController(IPantryInsightsService insights) : ControllerBase
{
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats(CancellationToken ct = default)
    {
        var stats = await insights.GetStatsAsync(ct);
        return Ok(stats);
    }
}
