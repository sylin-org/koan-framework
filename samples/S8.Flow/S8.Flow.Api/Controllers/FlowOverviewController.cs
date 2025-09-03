using Microsoft.AspNetCore.Mvc;

using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using S8.Flow.Shared;

namespace S8.Flow.Api.Controllers;

[ApiController]
[Route("api/flow/overview")]
public sealed class FlowOverviewController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        // Use model statics for counts
    var deviceCount = await Device.Count(ct);
    var sensorCount = await Sensor.Count(ct);
    var readingIntake = await Reading.CountAll("flow.intake", ct);
    var readingKeyed = await Reading.CountAll("flow.keyed", ct);
    var readingProcessed = await Reading.CountAll("flow.processed", ct);

        var result = new Dictionary<string, object>
        {
            ["devices"] = new Dictionary<string, int> { ["total"] = deviceCount },
            ["sensors"] = new Dictionary<string, int> { ["total"] = sensorCount },
            ["readings"] = new Dictionary<string, int>
            {
                ["intake"] = readingIntake,
                ["keyed"] = readingKeyed,
                ["processed"] = readingProcessed
            }
        };
        return Ok(result);
    }
}
