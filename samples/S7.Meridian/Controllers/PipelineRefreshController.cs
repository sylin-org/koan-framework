using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Data.Core;
using Koan.Samples.Meridian.Infrastructure;
using Koan.Samples.Meridian.Models;
using Koan.Samples.Meridian.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Koan.Samples.Meridian.Controllers;

[ApiController]
[Route(MeridianConstants.PipelineRefresh.Route)]
public sealed class PipelineRefreshController : ControllerBase
{
    private readonly IJobCoordinator _jobs;
    private readonly ILogger<PipelineRefreshController> _logger;

    public PipelineRefreshController(IJobCoordinator jobs, ILogger<PipelineRefreshController> logger)
    {
        _jobs = jobs;
        _logger = logger;
    }

    [HttpPost]
    public async Task<ActionResult> RefreshAsync(string pipelineId, CancellationToken ct)
    {
        var pipeline = await DocumentPipeline.Get(pipelineId, ct);
        if (pipeline is null)
        {
            return NotFound();
        }

        var docList = pipeline.DocumentIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var existing = await ProcessingJob.FindPendingAsync(pipelineId, ct).ConfigureAwait(false);

        if (docList.Count == 0)
        {
            if (existing is not null)
            {
                _logger.LogDebug("Pipeline {PipelineId} refresh reuse; no new documents but pending job {JobId} exists.", pipelineId, existing.Id);
                return Accepted(new { jobId = existing.Id, documentCount = existing.DocumentIds.Count });
            }

            return BadRequest(new { error = "No documents available for refresh." });
        }

        ProcessingJob job;
        if (existing is not null)
        {
            var before = existing.DocumentIds.Count;
            if (existing.MergeDocuments(docList))
            {
                var added = existing.DocumentIds.Count - before;
                existing.HeartbeatAt = DateTime.UtcNow;
                await existing.Save(ct).ConfigureAwait(false);
                _logger.LogInformation("Pipeline {PipelineId} refresh appended {Added} documents to pending job {JobId}.", pipelineId, added, existing.Id);
            }
            job = existing;
        }
        else
        {
            job = await _jobs.ScheduleAsync(pipelineId, docList, ct).ConfigureAwait(false);
        }

        pipeline.Status = PipelineStatus.Queued;
        pipeline.UpdatedAt = DateTime.UtcNow;
        await pipeline.Save(ct).ConfigureAwait(false);

        return Accepted(new { jobId = job.Id, documentCount = job.DocumentIds.Count });
    }
}
