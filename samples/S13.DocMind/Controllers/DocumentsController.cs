using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

    [HttpGet("stats")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult> GetStatsAsync(CancellationToken cancellationToken)
    {
        var documents = await SourceDocument.All(cancellationToken).ConfigureAwait(false);
        var documentList = documents.ToList();

        var stats = new
        {
            totalFiles = documentList.Count,
            totalFileSize = documentList.Sum(d => d.FileSizeBytes),
            processedFiles = documentList.Count(d => !string.IsNullOrEmpty(d.AssignedProfileId)),
            pendingFiles = documentList.Count(d => string.IsNullOrEmpty(d.AssignedProfileId))
        };

        return Ok(stats);
    }

    [HttpGet("recent")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult> GetRecentAsync(CancellationToken cancellationToken, [FromQuery] int limit = 10)
    {
        var documents = await SourceDocument.All(cancellationToken).ConfigureAwait(false);
        var recent = documents
            .OrderByDescending(d => d.UploadedAt)
            .Take(limit)
            .ToList();

        return Ok(recent);
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

        if (!Guid.TryParse(id, out var documentGuid))
        {
            return BadRequest();
        }

        var events = await QueryProcessingEventsAsync(documentGuid, cancellationToken).ConfigureAwait(false);
        var response = events
            .OrderBy(e => e.CreatedAt)
            .Select(e => new TimelineEntryResponse
            {
                Stage = e.Stage,
                Status = e.Status,
                Detail = e.Detail ?? string.Empty,
                CreatedAt = e.CreatedAt,
                Context = new Dictionary<string, string>(e.Context, StringComparer.OrdinalIgnoreCase),
                Metrics = new Dictionary<string, double>(e.Metrics)
            })
            .ToList();
        return Ok(response);
    }

    [HttpGet("{id}/chunks")]
    public async Task<ActionResult<IEnumerable<DocumentChunkResponse>>> GetChunksAsync([FromRoute] string id, [FromQuery] bool includeInsights, CancellationToken cancellationToken)
    {
        var document = await SourceDocument.Get(id, cancellationToken).ConfigureAwait(false);
        if (document is null) return NotFound();

        if (!Guid.TryParse(id, out var documentGuid))
        {
            return BadRequest();
        }

        var chunks = await DocumentChunk.Query($"SourceDocumentId == '{documentGuid}'", cancellationToken).ConfigureAwait(false);

        var response = chunks
            .OrderBy(c => c.Order)
            .Select(c => new DocumentChunkResponse
            {
                Id = c.Id,
                Order = c.Order,
                Text = c.Text,
                CharacterCount = c.CharacterCount,
                TokenCount = c.TokenCount,
                IsLastChunk = c.IsLastChunk,
                InsightRefs = includeInsights
                    ? c.InsightRefs.Select(refEntry => new InsightReferenceResponse
                        {
                            InsightId = refEntry.InsightId.ToString(),
                            Channel = refEntry.Channel,
                            Confidence = refEntry.Confidence,
                            Heading = refEntry.Heading
                        })
                        .ToList()
                    : Array.Empty<InsightReferenceResponse>()
            })
            .ToList();

        return Ok(response);
    }

    [HttpGet("{id}/insights")]
    public async Task<ActionResult<IEnumerable<DocumentInsightResponse>>> GetInsightsAsync([FromRoute] string id, [FromQuery] string? channel, CancellationToken cancellationToken)
    {
        var document = await SourceDocument.Get(id, cancellationToken).ConfigureAwait(false);
        if (document is null) return NotFound();

        if (!Guid.TryParse(id, out var documentGuid))
        {
            return BadRequest();
        }

        var query = string.IsNullOrWhiteSpace(channel)
            ? $"SourceDocumentId == '{documentGuid}'"
            : $"SourceDocumentId == '{documentGuid}' AND Channel == '{channel}'";
        var insights = await DocumentInsight.Query(query, cancellationToken).ConfigureAwait(false);
        var response = insights
            .OrderByDescending(i => i.GeneratedAt)
            .Select(i => new DocumentInsightResponse
            {
                Id = i.Id,
                Heading = i.Heading,
                Body = i.Body,
                Confidence = i.Confidence,
                Channel = i.Channel,
                Section = i.Section,
                GeneratedAt = i.GeneratedAt,
                StructuredPayload = new Dictionary<string, object?>(i.StructuredPayload),
                Metadata = new Dictionary<string, string>(i.Metadata, StringComparer.OrdinalIgnoreCase)
            })
            .ToList();

        return Ok(response);
}

    private static async Task<IReadOnlyList<DocumentProcessingEvent>> QueryProcessingEventsAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var filter = $"SourceDocumentId == '{documentId}'";
        var events = await DocumentProcessingEvent.Query(filter, cancellationToken).ConfigureAwait(false);
        return events
            .OrderBy(e => e.CreatedAt)
            .ToList();
    }
}
