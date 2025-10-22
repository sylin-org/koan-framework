using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Data.Core;
using Koan.Samples.Meridian.Infrastructure;
using Koan.Samples.Meridian.Models;
using Microsoft.AspNetCore.Mvc;

namespace Koan.Samples.Meridian.Controllers;

[ApiController]
[Route(MeridianConstants.PipelineRefresh.Route)]
public sealed class PipelineRefreshController : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult> RefreshAsync(string pipelineId, CancellationToken ct)
    {
        var pipeline = await DocumentPipeline.Get(pipelineId, ct);
        if (pipeline is null)
        {
            return NotFound();
        }

        var documents = await SourceDocument.Query(d => d.PipelineId == pipelineId, ct);
        var docList = documents.Select(d => d.Id).Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
        if (docList.Count == 0)
        {
            return BadRequest(new { error = "No documents available for refresh." });
        }

        var job = new ProcessingJob
        {
            PipelineId = pipelineId,
            DocumentIds = docList,
            Status = JobStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };
        await job.Save(ct);

        pipeline.Status = PipelineStatus.Queued;
        pipeline.UpdatedAt = DateTime.UtcNow;
        await pipeline.Save(ct);

        return Accepted(new { jobId = job.Id, documentCount = docList.Count });
    }
}
