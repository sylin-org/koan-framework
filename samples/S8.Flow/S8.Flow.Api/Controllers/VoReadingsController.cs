using Microsoft.AspNetCore.Mvc;
using S8.Flow.Shared;
using Sora.Data.Core;
using Sora.Flow.Infrastructure;
using Sora.Flow.Model;

namespace S8.Flow.Api.Controllers;

[ApiController]
[Route("api/vo-readings")] // GET api/vo-readings/{referenceId}?page=1&size=200
public sealed class VoReadingsController : ControllerBase
{
    [HttpGet("{referenceId}")]
    public async Task<IActionResult> Get(string referenceId, [FromQuery] int page = 1, [FromQuery] int size = 200, CancellationToken ct = default)
    {
        if (page < 1) page = 1; if (size < 1) size = 200; if (size > 1000) size = 1000;
        var items = new List<StageRecord<SensorReadingVo>>(capacity: size);
        // Try Keyed with simple paged scan + filter
        using (DataSetContext.With(FlowSets.StageShort(FlowSets.Keyed)))
        {
            var page1 = await StageRecord<SensorReadingVo>.FirstPage(size, ct);
            foreach (var r in page1)
                if (string.Equals(r.CorrelationId, referenceId, StringComparison.Ordinal)) items.Add(r);
        }
        // Fallback: Intake
        if (items.Count == 0)
        {
            using (DataSetContext.With(FlowSets.StageShort(FlowSets.Intake)))
            {
                var page1 = await StageRecord<SensorReadingVo>.FirstPage(size, ct);
                foreach (var r in page1)
                    if (string.Equals(r.CorrelationId, referenceId, StringComparison.Ordinal)) items.Add(r);
            }
        }
        // Fallback: stream Keyed
        if (items.Count == 0)
        {
            using (DataSetContext.With(FlowSets.StageShort(FlowSets.Keyed)))
            {
                var stream = StageRecord<SensorReadingVo>.AllStream(size, ct);
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
        var refItem = await ReferenceItem<SensorReadingVo>.GetByCanonicalId(canonicalId, ct);
        if (refItem is null) return NotFound();
        // Reuse Get handler with ULID id
        return await Get(refItem.Id, page, size, ct);
    }
}
