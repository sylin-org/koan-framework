using System;
using Microsoft.AspNetCore.Mvc;
using S13.DocMind.Services;

namespace S13.DocMind.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProcessingController : ControllerBase
{
    private readonly IDocumentProcessingDiagnostics _diagnostics;

    public ProcessingController(IDocumentProcessingDiagnostics diagnostics)
    {
        _diagnostics = diagnostics;
    }

    [HttpGet("queue")]
    public async Task<ActionResult> GetQueue(CancellationToken cancellationToken)
    {
        var queue = await _diagnostics.GetQueueAsync(cancellationToken);
        return Ok(queue);
    }

    [HttpGet("timeline")]
    public async Task<ActionResult> GetTimeline(
        [FromQuery] string? documentTypeId,
        [FromQuery] string? status,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken cancellationToken = default)
    {
        var query = new ProcessingTimelineQuery
        {
            DocumentTypeId = documentTypeId,
            Status = status,
            FromDate = from,
            ToDate = to
        };

        var timeline = await _diagnostics.GetTimelineAsync(query, cancellationToken);
        return Ok(timeline);
    }

    [HttpPost("{fileId}/retry")]
    public async Task<ActionResult> Retry(string fileId, [FromBody] ProcessingRetryRequest? request, CancellationToken cancellationToken)
    {
        var normalizedRequest = request ?? new ProcessingRetryRequest();
        var result = await _diagnostics.RetryAsync(fileId, normalizedRequest, cancellationToken);
        if (!result.Success)
        {
            return NotFound(new { message = result.Message });
        }

        return Ok(new
        {
            message = "Retry queued",
            fileId = result.FileId,
            fileStatus = result.FileStatus,
            analysisStatus = result.AnalysisStatus
        });
    }
}
