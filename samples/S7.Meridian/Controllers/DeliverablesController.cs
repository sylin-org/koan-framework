using Koan.Samples.Meridian.Infrastructure;
using Koan.Samples.Meridian.Models;
using Microsoft.AspNetCore.Mvc;

namespace Koan.Samples.Meridian.Controllers;

[ApiController]
[Route(MeridianConstants.Deliverables.Route)]
public sealed class DeliverablesController : ControllerBase
{
    [HttpGet("latest")]
    public async Task<ActionResult<Deliverable>> GetLatest(string pipelineId, CancellationToken ct)
    {
        var deliverables = await Deliverable.Query(d => d.PipelineId == pipelineId, ct);
        var latest = deliverables
            .OrderByDescending(d => d.CreatedAt)
            .FirstOrDefault();

        if (latest is null)
        {
            return NotFound();
        }

        return latest;
    }
}
