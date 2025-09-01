using Microsoft.AspNetCore.Mvc;
using S8.Flow.Shared;
using Sora.Flow.Model;
using Sora.Flow.Infrastructure;
using Sora.Data.Core;

namespace S8.Flow.Api.Controllers;

[ApiController]
[Route("api/readings")] // VO-only ingest: posts a SensorReading and routes to Sensor model intake
public sealed class ReadingsController : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Post([FromBody] SensorReading reading, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reading.SensorKey)) return BadRequest("sensorKey is required");

    var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [Keys.Sensor.Key] = reading.SensorKey,
            [Keys.Reading.Value] = reading.Value,
            [Keys.Reading.CapturedAt] = reading.CapturedAt.ToString("O"),
        };
        if (!string.IsNullOrWhiteSpace(reading.Unit)) payload[Keys.Sensor.Unit] = reading.Unit;
        if (!string.IsNullOrWhiteSpace(reading.Source)) payload[Keys.Reading.Source] = reading.Source!;

        var typed = new StageRecord<Sensor>
        {
            Id = Guid.NewGuid().ToString("n"),
            SourceId = reading.Source ?? "api",
            OccurredAt = reading.CapturedAt,
            StagePayload = payload
        };
        using (DataSetContext.With(FlowSets.StageShort(FlowSets.Intake)))
        {
            await Data<StageRecord<Sensor>, string>.UpsertAsync(typed, ct);
        }
        return Accepted();
    }
}
