using Microsoft.AspNetCore.Mvc;

using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Koan.Data.Abstractions;
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
        var deviceIntake = await Device.Count.Partition("flow.intake", CountStrategy.Optimized, ct);
        var deviceKeyed = await Device.Count.Partition("flow.keyed", CountStrategy.Optimized, ct);
        var deviceProcessed = await Device.Count.Partition("flow.processed", CountStrategy.Optimized, ct);
        var deviceTotal = await Device.Count;

        var sensorIntake = await Sensor.Count.Partition("flow.intake", CountStrategy.Optimized, ct);
        var sensorKeyed = await Sensor.Count.Partition("flow.keyed", CountStrategy.Optimized, ct);
        var sensorProcessed = await Sensor.Count.Partition("flow.processed", CountStrategy.Optimized, ct);
        var sensorTotal = await Sensor.Count;

        var readingIntake = await Reading.Count.Partition("flow.intake", CountStrategy.Optimized, ct);
        var readingKeyed = await Reading.Count.Partition("flow.keyed", CountStrategy.Optimized, ct);
        var readingProcessed = await Reading.Count.Partition("flow.processed", CountStrategy.Optimized, ct);
        var readingTotal = await Reading.Count;

        // Get manufacturer counts (may not have flow states yet)
        var manufacturerTotal = await Manufacturer.Count;
        var manufacturerIntake = await Manufacturer.Count.Partition("flow.intake", CountStrategy.Optimized, ct);
        var manufacturerKeyed = await Manufacturer.Count.Partition("flow.keyed", CountStrategy.Optimized, ct);
        var manufacturerProcessed = await Manufacturer.Count.Partition("flow.processed", CountStrategy.Optimized, ct);

        var result = new Dictionary<string, object>
        {
            ["devices"] = new Dictionary<string, long>
            {
                ["total"] = deviceTotal,
                ["intake"] = deviceIntake,
                ["keyed"] = deviceKeyed,
                ["processed"] = deviceProcessed
            },
            ["sensors"] = new Dictionary<string, long>
            {
                ["total"] = sensorTotal,
                ["intake"] = sensorIntake,
                ["keyed"] = sensorKeyed,
                ["processed"] = sensorProcessed
            },
            ["readings"] = new Dictionary<string, long>
            {
                ["total"] = readingTotal,
                ["intake"] = readingIntake,
                ["keyed"] = readingKeyed,
                ["processed"] = readingProcessed
            },
            ["manufacturers"] = new Dictionary<string, long>
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
