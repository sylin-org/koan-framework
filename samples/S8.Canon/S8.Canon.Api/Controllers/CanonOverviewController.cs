using Microsoft.AspNetCore.Mvc;

using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using S8.Canon.Shared;

namespace S8.Canon.Api.Controllers;

[ApiController]
[Route("api/flow/overview")]
public sealed class FlowOverviewController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        // Use model statics for counts across all flow states
        var deviceIntake = await Device.CountAll("flow.intake", ct);
        var deviceKeyed = await Device.CountAll("flow.keyed", ct);
        var deviceProcessed = await Device.CountAll("flow.processed", ct);
        var deviceTotal = await Device.Count(ct);
        
        var sensorIntake = await Sensor.CountAll("flow.intake", ct);
        var sensorKeyed = await Sensor.CountAll("flow.keyed", ct);
        var sensorProcessed = await Sensor.CountAll("flow.processed", ct);
        var sensorTotal = await Sensor.Count(ct);
        
        var readingIntake = await Reading.CountAll("flow.intake", ct);
        var readingKeyed = await Reading.CountAll("flow.keyed", ct);
        var readingProcessed = await Reading.CountAll("flow.processed", ct);
        var readingTotal = await Reading.Count(ct);

        // Get manufacturer counts (may not have flow states yet)
        var manufacturerTotal = await Manufacturer.Count(ct);
        var manufacturerIntake = await Manufacturer.CountAll("flow.intake", ct);
        var manufacturerKeyed = await Manufacturer.CountAll("flow.keyed", ct);
        var manufacturerProcessed = await Manufacturer.CountAll("flow.processed", ct);

        var result = new Dictionary<string, object>
        {
            ["devices"] = new Dictionary<string, int> 
            { 
                ["total"] = deviceTotal,
                ["intake"] = deviceIntake,
                ["keyed"] = deviceKeyed,
                ["processed"] = deviceProcessed
            },
            ["sensors"] = new Dictionary<string, int> 
            { 
                ["total"] = sensorTotal,
                ["intake"] = sensorIntake,
                ["keyed"] = sensorKeyed,
                ["processed"] = sensorProcessed
            },
            ["readings"] = new Dictionary<string, int>
            {
                ["total"] = readingTotal,
                ["intake"] = readingIntake,
                ["keyed"] = readingKeyed,
                ["processed"] = readingProcessed
            },
            ["manufacturers"] = new Dictionary<string, int>
            {
                ["total"] = manufacturerTotal,
                ["intake"] = manufacturerIntake,
                ["keyed"] = manufacturerKeyed,
                ["processed"] = manufacturerProcessed
            }
        };
        return Ok(result);
    }
}
