using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using S13.DocMind.Models;
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
    public async Task<ActionResult> GetQueue(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] DocumentProcessingStatus[]? status = null,
        [FromQuery] DocumentProcessingStage[]? stage = null,
        [FromQuery] string? correlationId = null,
        [FromQuery] string? documentId = null,
        [FromQuery] bool includeCompleted = false,
        [FromQuery] bool includeFuture = false,
        CancellationToken cancellationToken = default)
    {
        var query = new ProcessingQueueQuery
        {
            Page = page,
            PageSize = pageSize,
            Statuses = status,
            Stages = stage,
            CorrelationId = correlationId,
            DocumentId = documentId,
            IncludeCompleted = includeCompleted,
            IncludeFuture = includeFuture
        };

        var queue = await _diagnostics.GetQueueAsync(query, cancellationToken);
        return Ok(queue);
    }

    [HttpGet("timeline")]
    public async Task<ActionResult> GetTimeline(
        [FromQuery] string? documentId,
        [FromQuery] DocumentProcessingStage? stage,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        CancellationToken cancellationToken = default)
    {
        var query = new ProcessingTimelineQuery
        {
            DocumentId = documentId,
            Stage = stage,
            From = from,
            To = to
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
            documentId = result.DocumentId,
            status = result.Status
        });
    }
}
