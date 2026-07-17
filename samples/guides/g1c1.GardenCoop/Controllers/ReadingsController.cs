using g1c1.GardenCoop.Automation;
using g1c1.GardenCoop.Infrastructure;
using g1c1.GardenCoop.Models;
using Koan.Web.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace g1c1.GardenCoop.Controllers;

[Route(GardenApiRoutes.Readings)]
public sealed class ReadingsController : EntityController<Reading>
{
    [HttpGet(GardenApiRoutes.RecentReadings)]
    public async Task<ActionResult<IReadOnlyList<Reading>>> GetRecent(
        [FromRoute] string plotId,
        [FromQuery] int take = GardenAutomation.ReadingWindowSize,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(plotId))
        {
            return BadRequest("plotId is required.");
        }

        return Ok(await Reading.Recent(plotId, take, cancellationToken));
    }
}
