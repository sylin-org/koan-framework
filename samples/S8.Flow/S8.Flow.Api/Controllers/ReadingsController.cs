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
    // Recent readings across the pipeline (defaults to Keyed, falls back to Intake)
    [HttpGet]
    public async Task<IActionResult> GetRecent([FromQuery] int size = 50, CancellationToken ct = default)
    {
        if (size < 1) size = 50; if (size > 1000) size = 1000;
        var items = new List<StageRecord<Reading>>(capacity: size);

        // Prefer Keyed stage for stabilized, correlated readings
        using (DataSetContext.With(FlowSets.StageShort(FlowSets.Keyed)))
        {
            var page1 = await StageRecord<Reading>.FirstPage(size, ct);
            items.AddRange(page1);
        }

        // Fallback to Intake if nothing in Keyed yet
        if (items.Count == 0)
        {
            using (DataSetContext.With(FlowSets.StageShort(FlowSets.Intake)))
            {
                var page1 = await StageRecord<Reading>.FirstPage(size, ct);
                items.AddRange(page1);
            }
        }

        var list = items
            .OrderByDescending(r => r.OccurredAt)
            .Take(size)
            .Select(r => new
            {
                id = r.Id,
                at = r.OccurredAt,
                source = r.SourceId,
                payload = r.StagePayload,
                correlationId = r.CorrelationId
            })
            .ToList();

        return Ok(new { page = 1, size, returned = list.Count, items = list });
    }

    [HttpGet("{referenceId}")]
    public async Task<IActionResult> Get(string referenceId, [FromQuery] int page = 1, [FromQuery] int size = 200, CancellationToken ct = default)
    {
        if (page < 1) page = 1; if (size < 1) size = 200; if (size > 1000) size = 1000;
        var items = new List<StageRecord<Reading>>(capacity: size);
        // Try Keyed with simple paged scan + filter
        using (DataSetContext.With(FlowSets.StageShort(FlowSets.Keyed)))
        {
            var page1 = await StageRecord<Reading>.FirstPage(size, ct);
            foreach (var r in page1)
                if (string.Equals(r.CorrelationId, referenceId, StringComparison.Ordinal)) items.Add(r);
        }
        // Fallback: Intake
        if (items.Count == 0)
        {
            using (DataSetContext.With(FlowSets.StageShort(FlowSets.Intake)))
            {
                var page1 = await StageRecord<Reading>.FirstPage(size, ct);
                foreach (var r in page1)
                    if (string.Equals(r.CorrelationId, referenceId, StringComparison.Ordinal)) items.Add(r);
            }
        }
        // Fallback: stream Keyed
        if (items.Count == 0)
        {
            using (DataSetContext.With(FlowSets.StageShort(FlowSets.Keyed)))
            {
                var stream = StageRecord<Reading>.AllStream(size, ct);
                await foreach (var r in stream.WithCancellation(ct))
                {
                    if (string.Equals(r.CorrelationId, referenceId, StringComparison.Ordinal))
                    {
                        items.Add(r);
                        if (items.Count >= size) break;
                    }
                }
            }
        }

        var list = items
            .OrderBy(r => r.OccurredAt)
            .Take(size)
            .Select(r => new
            {
                id = r.Id,
                at = r.OccurredAt,
                source = r.SourceId,
                payload = r.StagePayload
            })
            .ToList();

        return Ok(new { page, size, returned = list.Count, items = list });
    }

    // Secondary route: resolve by CanonicalId (business key) and use ULID for correlation
    [HttpGet("by-cid/{canonicalId}")]
    public async Task<IActionResult> GetByCanonicalId(string canonicalId, [FromQuery] int page = 1, [FromQuery] int size = 200, CancellationToken ct = default)
    {
        var refItem = await ReferenceItem<Reading>.GetByCanonicalId(canonicalId, ct);
        if (refItem is null) return NotFound();
        // Reuse Get handler with ULID id
        return await Get(refItem.Id, page, size, ct);
    }

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

        var typed = new StageRecord<Reading>
        {
            Id = Guid.NewGuid().ToString("n"),
            SourceId = reading.Source ?? FlowSampleConstants.Sources.Api,
            OccurredAt = reading.CapturedAt,
            StagePayload = payload,
            CorrelationId = reading.SensorKey
        };
        using (DataSetContext.With(FlowSets.StageShort(FlowSets.Intake)))
        {
            await Data<StageRecord<Reading>, string>.UpsertAsync(typed, ct);
        }
        return Accepted();
    }
}
