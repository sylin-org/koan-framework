using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using g1c1.GardenCoop.Models;
using Koan.Web.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace g1c1.GardenCoop.Controllers;

// EntityController<Reading> gives us GET, POST, PATCH, DELETE for free - no manual wiring!
[Route("api/garden/readings")]
public sealed class ReadingsController : EntityController<Reading>
{
	// EntityController already handles:
	// - GET /api/garden/readings (all)
	// - GET /api/garden/readings/{id} (by id)
	// - POST /api/garden/readings (create - the Pi uses this!)
	// - PATCH /api/garden/readings/{id} (update)
	// - DELETE /api/garden/readings/{id} (delete)

	// just adding a helper endpoint for recent readings by plot
	[HttpGet("recent/{plotId}")]
	public async Task<IActionResult> GetRecent(string plotId, [FromQuery] int take = 8, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(plotId))
		{
			return BadRequest("plotId is required.");
		}

		// using the static helper from Reading model
		var readings = await Reading.Recent(plotId, take, cancellationToken);
		return Ok(readings);
	}

	// Note: Pi POSTs to /api/garden/readings with JSON like:
	// {
	//   "sensorSerial": "gc-013-alfalfa",
	//   "soilHumidity": 18.6,
	//   "temperatureC": 26.1,
	//   "sampledAt": "2025-09-28T09:15:00Z"
	// }
	// The Reading.BeforeUpsert lifecycle handles sensor lookup and binding!
}
