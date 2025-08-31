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
        using (DataSetContext.With(FlowSets.StageShort(FlowSets.Keyed)))
        {
            // Page keyed Sensor stage records and filter by CorrelationId == referenceId
            var items = await StageRecord<Sensor>.Page(page, size, ct);
            var list = items
                .Where(r => string.Equals(r.CorrelationId, referenceId, StringComparison.Ordinal))
                .OrderBy(r => r.OccurredAt)
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
    }
}
