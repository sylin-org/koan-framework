using Koan.Samples.Meridian.Infrastructure;
using Koan.Samples.Meridian.Models;
using Microsoft.AspNetCore.Mvc;

namespace Koan.Samples.Meridian.Controllers;

[ApiController]
[Route(MeridianConstants.Jobs.Route)]
public sealed class JobsController : ControllerBase
{
    [HttpGet("{jobId}")]
    public async Task<ActionResult<ProcessingJob>> Get(string pipelineId, string jobId, CancellationToken ct)
    {
        var job = await ProcessingJob.Get(jobId, ct);
        if (job == null || !string.Equals(job.PipelineId, pipelineId, StringComparison.Ordinal))
        {
            return NotFound();
        }

        return job;
    }
}
