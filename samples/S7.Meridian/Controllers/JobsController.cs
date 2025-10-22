using System;
using Koan.Data.Core;
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
        // Check main collection first
        var job = await ProcessingJob.Get(jobId, ct);
        
        // If not found, check completed-jobs partition
        if (job == null)
        {
            job = await Data<ProcessingJob, string>.GetAsync(jobId, "completed-jobs", ct);
        }
        
        if (job == null || !string.Equals(job.PipelineId, pipelineId, StringComparison.Ordinal))
        {
            return NotFound();
        }

        return job;
    }

    [HttpPost("{jobId}/cancel")]
    public async Task<ActionResult<ProcessingJob>> Cancel(string pipelineId, string jobId, CancellationToken ct)
    {
        var (job, cancelled) = await ProcessingJob.TryCancelPendingAsync(jobId, ct).ConfigureAwait(false);
        if (job is null || !string.Equals(job.PipelineId, pipelineId, StringComparison.Ordinal))
        {
            return NotFound();
        }

        if (!cancelled)
        {
            var reason = job.Status switch
            {
                JobStatus.Processing => "Job is actively processing; wait for the heartbeat window to elapse or stop the worker before cancelling.",
                JobStatus.Completed => "Job has already completed.",
                JobStatus.Cancelled => "Job is already cancelled.",
                JobStatus.Failed => "Job has already failed.",
                _ => "Job cannot be cancelled in its current state."
            };

            return Conflict(new { error = reason });
        }

        var pipeline = await DocumentPipeline.Get(pipelineId, ct).ConfigureAwait(false);
        if (pipeline is not null)
        {
            pipeline.Status = PipelineStatus.Pending;
            pipeline.UpdatedAt = DateTime.UtcNow;
            await pipeline.Save(ct).ConfigureAwait(false);
        }

        return Accepted(job);
    }
}
