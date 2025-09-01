using Microsoft.AspNetCore.Mvc;
using S8.Flow.Shared;
using Sora.Flow.Model;
using Sora.Flow.Infrastructure;
using Sora.Data.Core;

namespace S8.Flow.Api.Controllers;

[ApiController]
[Route("api/readings")] // VO-only ingest: posts a SensorReading and routes to VO StageRecord intake
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

        var typed = new StageRecord<SensorReadingVo>
        {
            Id = Guid.NewGuid().ToString("n"),
            SourceId = reading.Source ?? "api",
            OccurredAt = reading.CapturedAt,
            StagePayload = payload,
            CorrelationId = reading.SensorKey
        };
        using (DataSetContext.With(FlowSets.StageShort(FlowSets.Intake)))
        {
            await Data<StageRecord<SensorReadingVo>, string>.UpsertAsync(typed, ct);
        }
        return Accepted();
    }
}
