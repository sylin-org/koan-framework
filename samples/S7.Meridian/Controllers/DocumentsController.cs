using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Data.Core;
using Koan.Samples.Meridian.Infrastructure;
using Koan.Samples.Meridian.Models;
using Koan.Samples.Meridian.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace Koan.Samples.Meridian.Controllers;

[ApiController]
[Route(MeridianConstants.Documents.Route)]
public sealed class DocumentsController : ControllerBase
{
    private readonly IDocumentIngestionService _ingestion;
    private readonly IJobCoordinator _jobs;
    private readonly IRunLogWriter _runLog;
    private readonly ILogger<DocumentsController> _logger;

    public DocumentsController(IDocumentIngestionService ingestion, IJobCoordinator jobs, IRunLogWriter runLog, ILogger<DocumentsController> logger)
    {
        _ingestion = ingestion;
        _jobs = jobs;
        _runLog = runLog;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<List<SourceDocument>>> GetDocuments(string pipelineId, CancellationToken ct)
    {
        var pipeline = await DocumentPipeline.Get(pipelineId, ct).ConfigureAwait(false);
        if (pipeline is null)
        {
            return NotFound();
        }

        var documents = await pipeline.LoadDocumentsAsync(ct).ConfigureAwait(false);
        return Ok(documents);
    }

    [HttpPost]
    [RequestSizeLimit(200_000_000)]
    public async Task<ActionResult<DocumentIngestionResponse>> Upload(string pipelineId, [FromForm] List<IFormFile>? files, [FromQuery] bool force, [FromQuery] string? typeHint, CancellationToken ct)
    {
        var collected = new FormFileCollection();

        if (files is { Count: > 0 })
        {
            foreach (var candidate in files)
            {
                if (candidate is { Length: > 0 })
                {
                    collected.Add(candidate);
                }
            }
        }

        if (collected.Count == 0 && Request.HasFormContentType)
        {
            foreach (var formFile in Request.Form.Files)
            {
                if (formFile is { Length: > 0 })
                {
                    collected.Add(formFile);
                }
            }
        }

        if (collected.Count == 0)
        {
            return BadRequest("At least one file is required.");
        }

    var result = await _ingestion.IngestAsync(pipelineId, collected, force, typeHint, ct).ConfigureAwait(false);
        var newIds = result.NewDocuments
            .Select(d => d.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToList();

        var reusedIds = result.ReusedDocuments
            .Select(d => d.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToList();

        if (newIds.Count == 0)
        {
            _logger.LogInformation(
                "Upload response for pipeline {PipelineId}: Only reused documents ({Count}). Returning reused IDs: [{ReusedIds}]",
                pipelineId, reusedIds.Count, string.Join(", ", reusedIds));

            var reuseResponse = new DocumentIngestionResponse
            {
                DocumentId = reusedIds.FirstOrDefault() ?? string.Empty,
                DocumentIds = reusedIds,
                ReusedDocumentIds = reusedIds,
                Status = "Uploaded"
            };

            return Ok(reuseResponse);
        }

        // Documents uploaded and staged - processing happens on explicit Refresh
        _logger.LogInformation(
            "Upload response for pipeline {PipelineId}: {NewCount} new, {ReusedCount} reused. Returning new IDs: [{NewIds}], reused: [{ReusedIds}]",
            pipelineId, newIds.Count, reusedIds.Count, string.Join(", ", newIds), string.Join(", ", reusedIds));

        var response = new DocumentIngestionResponse
        {
            DocumentId = newIds.FirstOrDefault() ?? string.Empty,
            DocumentIds = newIds,
            ReusedDocumentIds = reusedIds,
            JobId = null, // No auto-processing
            Status = "Uploaded"
        };

        return Ok(response);
    }

    [HttpPut("{documentId}/type")]
    [ProducesResponseType(typeof(DocumentTypeOverrideResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> OverrideDocumentType(
        string pipelineId,
        string documentId,
        [FromBody] DocumentTypeOverrideRequest request,
        CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.TypeId))
        {
            return BadRequest("TypeId is required.");
        }

        var pipeline = await DocumentPipeline.Get(pipelineId, ct).ConfigureAwait(false);
        if (pipeline is null)
        {
            return NotFound();
        }

        if (!pipeline.DocumentIds.Contains(documentId, StringComparer.Ordinal))
        {
            return NotFound();
        }

        var document = await SourceDocument.Get(documentId, ct).ConfigureAwait(false);
        if (document is null)
        {
            return NotFound();
        }

        var previousType = document.SourceType;
        var previousMethod = document.ClassificationMethod;

        document.SourceType = request.TypeId;
        document.ClassifiedTypeId = request.TypeId;
        document.ClassifiedTypeVersion = request.TypeVersion ?? document.ClassifiedTypeVersion ?? 1;
        document.ClassificationConfidence = Math.Clamp(request.Confidence ?? 1.0, 0.0, 1.0);
        document.ClassificationMethod = ClassificationMethod.Manual;
        document.ClassificationReason = string.IsNullOrWhiteSpace(request.Reason)
            ? "User override"
            : request.Reason!.Trim();
        document.Status = DocumentProcessingStatus.Pending;
        document.UpdatedAt = DateTime.UtcNow;

        var saved = await document.Save(ct).ConfigureAwait(false);

        await _runLog.AppendAsync(new RunLog
        {
            PipelineId = pipeline.Id ?? string.Empty,
            Stage = "classify-override",
            DocumentId = saved.Id,
            StartedAt = DateTime.UtcNow,
            FinishedAt = DateTime.UtcNow,
            Status = "override",
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["previousType"] = previousType,
                ["previousMethod"] = previousMethod.ToString(),
                ["newType"] = saved.SourceType,
                ["confidence"] = saved.ClassificationConfidence.ToString("0.00", CultureInfo.InvariantCulture),
                ["reason"] = saved.ClassificationReason ?? string.Empty
            }
        }, ct).ConfigureAwait(false);

        var job = await _jobs.ScheduleAsync(pipeline.Id!, new[] { saved.Id! }, ct).ConfigureAwait(false);

        var response = new DocumentTypeOverrideResponse
        {
            PipelineId = pipeline.Id!,
            DocumentId = saved.Id!,
            TypeId = saved.SourceType,
            JobId = job.Id!,
            Status = job.Status.ToString()
        };

        return Accepted(response);
    }
}

public sealed class DocumentIngestionResponse
{
    public string DocumentId { get; set; } = string.Empty;
    public List<string> DocumentIds { get; set; } = new();
    public List<string> ReusedDocumentIds { get; set; } = new();
    public string JobId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public sealed class DocumentTypeOverrideRequest
{
    public string TypeId { get; set; } = string.Empty;
    public double? Confidence { get; set; }
        = null;
    public int? TypeVersion { get; set; }
        = null;
    public string? Reason { get; set; }
        = null;
}

public sealed class DocumentTypeOverrideResponse
{
    public string PipelineId { get; set; } = string.Empty;
    public string DocumentId { get; set; } = string.Empty;
    public string TypeId { get; set; } = string.Empty;
    public string JobId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}
