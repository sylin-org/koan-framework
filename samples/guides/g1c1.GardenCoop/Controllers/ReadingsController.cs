using g1c1.GardenCoop.Models;
using Koan.Web.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace g1c1.GardenCoop.Controllers;

[Route("api/garden/readings")]
public sealed class ReadingsController : EntityController<Reading>
{
	[HttpGet("recent/{plotId}")]
	public async Task<IActionResult> GetRecent(string plotId, [FromQuery] int take = 8, CancellationToken cancellationToken = default)
	{
		var readings = await Reading.Recent(plotId, take, cancellationToken);
		return Ok(readings);
	}
}
