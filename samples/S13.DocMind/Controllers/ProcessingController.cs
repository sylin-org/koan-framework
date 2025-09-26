using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using S13.DocMind.Contracts;
using S13.DocMind.Infrastructure;
using S13.DocMind.Models;
using S13.DocMind.Services;

namespace S13.DocMind.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class ProcessingController : ControllerBase
{
    private readonly IDocumentIntakeService _intake;
    private readonly DocMindProcessingOptions _processingOptions;
    private readonly ILogger<ProcessingController> _logger;

    public ProcessingController(IDocumentIntakeService intake, IOptions<DocMindProcessingOptions> processingOptions, ILogger<ProcessingController> logger)
    {
        _intake = intake;
        _processingOptions = processingOptions.Value;
        _logger = logger;
    }

    [HttpPost("replay")]
    public async Task<IActionResult> ReplayAsync([FromBody] ProcessingReplayRequest request, CancellationToken cancellationToken)
    {
        var document = await SourceDocument.Get(request.DocumentId, cancellationToken).ConfigureAwait(false);
        if (document is null) return NotFound();

        document.MarkStatus(DocumentProcessingStatus.Uploaded);
        await document.Save(cancellationToken).ConfigureAwait(false);
        await _intake.RequeueAsync(document.Id, DocumentProcessingStage.Upload, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Replayed document {DocumentId}", document.Id);
        return Accepted(new { document.Id, document.Status });
    }

    [HttpGet("config")]
    public ActionResult<object> GetConfig()
        => Ok(new
        {
            _processingOptions.QueueCapacity,
            _processingOptions.MaxConcurrency,
            _processingOptions.ChunkSizeTokens,
            _processingOptions.EnableVisionExtraction
        });

    [HttpGet("events")]
    public async Task<ActionResult<IEnumerable<TimelineEntryResponse>>> GetRecentEventsAsync([FromQuery] int take = 25, CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 200);
        var events = await DocumentProcessingEvent.All(cancellationToken).ConfigureAwait(false);
        var response = events
            .OrderByDescending(e => e.CreatedAt)
            .Take(take)
            .Select(e => new TimelineEntryResponse
            {
                Stage = e.Stage,
                Status = e.Status,
                Message = e.Message,
                CreatedAt = e.CreatedAt,
                Context = e.Context
            })
            .ToList();
        return Ok(response);
    }
}
