using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using S13.DocMind.Contracts;
using S13.DocMind.Models;
using S13.DocMind.Services;
using Koan.Web.Controllers;

namespace S13.DocMind.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class DocumentsController : EntityController<SourceDocument>
{
    private readonly IDocumentIntakeService _intake;
    private readonly ILogger<DocumentsController> _logger;

    public DocumentsController(IDocumentIntakeService intake, ILogger<DocumentsController> logger)
    {
        _intake = intake;
        _logger = logger;
    }

    [HttpPost("upload")]
    [RequestSizeLimit(20_000_000)]
    [ProducesResponseType(typeof(DocumentUploadReceipt), StatusCodes.Status201Created)]
    public async Task<ActionResult<DocumentUploadReceipt>> UploadAsync([FromForm] UploadDocumentRequest request, CancellationToken cancellationToken)
    {
        var receipt = await _intake.UploadAsync(request, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Document {DocumentId} uploaded", receipt.DocumentId);
        return CreatedAtAction(nameof(GetById), new { id = receipt.DocumentId }, receipt);
    }

    [HttpPost("{id}/assign-profile")]
    public async Task<IActionResult> AssignProfileAsync([FromRoute] string id, [FromBody] AssignProfileRequest request, CancellationToken cancellationToken)
    {
        var document = await SourceDocument.Get(id, cancellationToken).ConfigureAwait(false);
        if (document is null) return NotFound();

        await _intake.AssignProfileAsync(document, request.ProfileId, request.AcceptSuggestion, cancellationToken).ConfigureAwait(false);
        return Accepted(new { document.Id, document.AssignedProfileId, document.AssignedBySystem });
    }

    [HttpGet("{id}/timeline")]
    public async Task<ActionResult<IEnumerable<TimelineEntryResponse>>> GetTimelineAsync([FromRoute] string id, CancellationToken cancellationToken)
    {
        var document = await SourceDocument.Get(id, cancellationToken).ConfigureAwait(false);
        if (document is null) return NotFound();

        var events = await DocumentProcessingEvent.Query($"DocumentId == '{id}'", cancellationToken).ConfigureAwait(false);
        var response = events
            .OrderBy(e => e.CreatedAt)
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

    [HttpGet("{id}/chunks")]
    public async Task<ActionResult<IEnumerable<DocumentChunkResponse>>> GetChunksAsync([FromRoute] string id, [FromQuery] bool includeInsights, CancellationToken cancellationToken)
    {
        var document = await SourceDocument.Get(id, cancellationToken).ConfigureAwait(false);
        if (document is null) return NotFound();

        var chunks = await DocumentChunk.Query($"DocumentId == '{id}'", cancellationToken).ConfigureAwait(false);
        List<DocumentInsight>? insights = null;
        Dictionary<string, List<DocumentInsightResponse>>? lookup = null;
        if (includeInsights)
        {
            insights = await DocumentInsight.Query($"DocumentId == '{id}'", cancellationToken).ConfigureAwait(false);
            lookup = insights
                .GroupBy(i => i.ChunkId ?? string.Empty)
                .ToDictionary(g => g.Key, g => g
                    .Select(i => new DocumentInsightResponse
                    {
                        Id = i.Id,
                        Title = i.Title,
                        Content = i.Content,
                        Confidence = i.Confidence,
                        Channel = i.Channel,
                        CreatedAt = i.CreatedAt
                    })
                    .ToList());
        }

        var response = chunks
            .OrderBy(c => c.Index)
            .Select(c => new DocumentChunkResponse
            {
                Id = c.Id,
                Index = c.Index,
                Channel = c.Channel,
                Content = c.Content,
                Summary = c.Summary,
                TokenEstimate = c.TokenEstimate,
                Insights = lookup is null ? Array.Empty<DocumentInsightResponse>() : lookup.TryGetValue(c.Id, out var entries) ? entries : Array.Empty<DocumentInsightResponse>()
            })
            .ToList();

        return Ok(response);
    }

    [HttpGet("{id}/insights")]
    public async Task<ActionResult<IEnumerable<DocumentInsightResponse>>> GetInsightsAsync([FromRoute] string id, [FromQuery] string? channel, CancellationToken cancellationToken)
    {
        var document = await SourceDocument.Get(id, cancellationToken).ConfigureAwait(false);
        if (document is null) return NotFound();

        var query = string.IsNullOrWhiteSpace(channel)
            ? $"DocumentId == '{id}'"
            : $"DocumentId == '{id}' AND Channel == '{channel}'";
        var insights = await DocumentInsight.Query(query, cancellationToken).ConfigureAwait(false);
        var response = insights
            .OrderByDescending(i => i.CreatedAt)
            .Select(i => new DocumentInsightResponse
            {
                Id = i.Id,
                Title = i.Title,
                Content = i.Content,
                Confidence = i.Confidence,
                Channel = i.Channel,
                CreatedAt = i.CreatedAt
            })
            .ToList();

        return Ok(response);
    }
}
