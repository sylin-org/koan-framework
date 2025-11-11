using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Data.Core;
using Koan.Samples.Meridian.Infrastructure;
using Koan.Samples.Meridian.Contracts;
using Koan.Samples.Meridian.Models;
using Koan.Samples.Meridian.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Koan.Web.Controllers;
using Newtonsoft.Json.Linq;

namespace Koan.Samples.Meridian.Controllers;

[ApiController]
[Route(MeridianConstants.Pipelines.Route)]
public sealed class PipelinesController : EntityController<DocumentPipeline>
{
    private readonly IPipelineBootstrapService _bootstrapService;
    private readonly IJobCoordinator _jobs;
    private readonly IRunLogWriter _runLog;
    private readonly ITemplateRenderer _renderer;
    private readonly ILogger<PipelinesController> _logger;

    public PipelinesController(
        IPipelineBootstrapService bootstrapService,
        IJobCoordinator jobs,
        IRunLogWriter runLog,
        ITemplateRenderer renderer,
        ILogger<PipelinesController> logger)
    {
        _bootstrapService = bootstrapService;
        _jobs = jobs;
        _runLog = runLog;
        _renderer = renderer;
        _logger = logger;
    }

    [HttpGet("{pipelineId}/graph")]
    [ProducesResponseType(typeof(PipelineGraphResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPipelineGraph(string pipelineId, CancellationToken ct = default)
    {
        var pipeline = await DocumentPipeline.Get(pipelineId, ct).ConfigureAwait(false);
        if (pipeline is null)
        {
            return NotFound();
        }

        var documents = await pipeline.LoadDocumentsAsync(ct).ConfigureAwait(false);

        _logger.LogInformation(
            "GetPipelineGraph for {PipelineId}: Pipeline.DocumentIds={PipelineDocIds}, LoadedDocuments={LoadedCount}, LoadedIds=[{LoadedIds}], LoadedStatuses=[{Statuses}]",
            pipelineId,
            pipeline.DocumentIds.Count,
            documents.Count,
            string.Join(", ", documents.Select(d => d.Id)),
            string.Join(", ", documents.Select(d => $"{d.Id}:{d.Status}")));

        var deliverable = await GetLatestDeliverableAsync(pipeline, ct).ConfigureAwait(false);

        JToken? canonical = null;
        if (deliverable is not null)
        {
            var canonicalJson = await _renderer.RenderJsonAsync(deliverable, ct).ConfigureAwait(false);
            canonical = TryParseCanonical(canonicalJson);
        }

        var analysisTypeName = string.Empty;
        if (!string.IsNullOrWhiteSpace(pipeline.AnalysisTypeId))
        {
            var analysisType = await AnalysisType.Get(pipeline.AnalysisTypeId, ct).ConfigureAwait(false);
            analysisTypeName = analysisType?.Name ?? string.Empty;
        }

        var pipelineKey = pipeline.Id ?? pipelineId;

        var jobSnapshots = await PipelineSnapshotMapper.LoadJobSnapshotsAsync(pipelineKey, ct).ConfigureAwait(false);
        var runSnapshots = await PipelineSnapshotMapper.LoadRunLogSnapshotsAsync(pipelineKey, ct).ConfigureAwait(false);

        var graph = new PipelineGraph
        {
            Pipeline = new PipelineSummary
            {
                Id = pipeline.Id ?? string.Empty,
                Name = pipeline.Name,
                Description = pipeline.Description,
                DeliverableTypeId = pipeline.DeliverableTypeId,
                DeliverableTypeVersion = pipeline.DeliverableTypeVersion,
                AnalysisTypeId = pipeline.AnalysisTypeId,
                AnalysisTypeVersion = pipeline.AnalysisTypeVersion,
                AnalysisTypeName = analysisTypeName,
                AnalysisTags = pipeline.AnalysisTags ?? new List<string>(),
                Status = pipeline.Status.ToString(),
                DocumentIds = pipeline.DocumentIds ?? new List<string>(),
                CreatedAt = pipeline.CreatedAt,
                UpdatedAt = pipeline.UpdatedAt,
                CompletedAt = pipeline.CompletedAt,
                DocumentCount = documents.Count
            },
            Documents = documents
                .Select(doc => new DocumentSummary
                {
                    Id = doc.Id ?? string.Empty,
                    OriginalFileName = doc.OriginalFileName,
                    SourceType = doc.SourceType,
                    ClassifiedTypeId = doc.ClassifiedTypeId,
                    ClassifiedTypeVersion = doc.ClassifiedTypeVersion,
                    ClassificationConfidence = doc.ClassificationConfidence,
                    Status = doc.Status.ToString(),
                    IsVirtual = doc.IsVirtual,
                    PageCount = doc.PageCount,
                    Size = doc.Size,
                    UploadedAt = doc.UploadedAt,
                    UpdatedAt = doc.UpdatedAt
                })
                .ToArray(),
            Deliverable = deliverable is null
                ? null
                : new DeliverableSnapshot
                {
                    Id = deliverable.Id ?? string.Empty,
                    DeliverableTypeId = deliverable.DeliverableTypeId,
                    DeliverableTypeVersion = deliverable.DeliverableTypeVersion,
                    DataHash = deliverable.DataHash,
                    TemplateMdHash = deliverable.TemplateMdHash,
                    Version = deliverable.Version,
                    CreatedAt = deliverable.CreatedAt,
                    FinalizedAt = deliverable.FinalizedAt,
                    FinalizedBy = deliverable.FinalizedBy,
                    RenderedMarkdown = deliverable.RenderedMarkdown,
                    RenderedPdfKey = deliverable.RenderedPdfKey,
                    SourceDocumentIds = deliverable.SourceDocumentIds ?? new List<string>()
                },
            Canonical = canonical,
            Notes = new PipelineNotesSnapshot
            {
                AuthoritativeNotes = pipeline.AuthoritativeNotes,
                UpdatedAt = pipeline.AuthoritativeNotes is null ? null : pipeline.UpdatedAt
            },
            Quality = MapQuality(pipeline.Quality),
            Jobs = jobSnapshots,
            Runs = runSnapshots
        };

        return Ok(new PipelineGraphResponse { Graph = graph });
    }

    /// <summary>
    /// Creates a pipeline from file-only payload with embedded configuration.
    /// POST /api/pipelines/create
    /// Content-Type: multipart/form-data
    /// Files: analysis-config.json (required), plus one or more documents
    /// </summary>
    [HttpPost("create")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(CreatePipelineResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateFromFiles(
        [FromForm] IFormFileCollection files,
        CancellationToken ct = default)
    {
        try
        {
            var response = await _bootstrapService.CreateFromFilesAsync(files, ct);
            return Created($"/api/pipelines/{response.PipelineId}", response);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid Request",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
    }

    [HttpPut("{pipelineId}/analysisType")]
    [ProducesResponseType(typeof(AnalysisTypeOverrideResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> OverrideAnalysisType(
        string pipelineId,
        [FromBody] AnalysisTypeOverrideRequest request,
        CancellationToken ct = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.AnalysisTypeId))
        {
            return BadRequest("AnalysisTypeId is required.");
        }

        var pipeline = await DocumentPipeline.Get(pipelineId, ct).ConfigureAwait(false);
        if (pipeline is null)
        {
            return NotFound();
        }

        var analysisType = await AnalysisType.Get(request.AnalysisTypeId, ct).ConfigureAwait(false);
        if (analysisType is null)
        {
            return NotFound();
        }

        var previousAnalysis = pipeline.AnalysisTypeId;
        var previousVersion = pipeline.AnalysisTypeVersion;

        pipeline.AnalysisTypeId = analysisType.Id!;
        pipeline.Status = PipelineStatus.Pending;
        pipeline.UpdatedAt = DateTime.UtcNow;

        await pipeline.Save(ct).ConfigureAwait(false);

        await _runLog.AppendAsync(new RunLog
        {
            PipelineId = pipeline.Id ?? string.Empty,
            Stage = "analysis-override",
            StartedAt = DateTime.UtcNow,
            FinishedAt = DateTime.UtcNow,
            Status = "override",
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["previousAnalysisType"] = previousAnalysis,
                ["previousVersion"] = previousVersion.ToString(CultureInfo.InvariantCulture),
                ["newAnalysisType"] = pipeline.AnalysisTypeId,
                ["newVersion"] = pipeline.AnalysisTypeVersion.ToString(CultureInfo.InvariantCulture),
                ["overrideReason"] = request.Reason ?? string.Empty
            }
        }, ct).ConfigureAwait(false);

        var documents = pipeline.DocumentIds.Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
        ProcessingJob? job = null;
        if (documents.Count > 0)
        {
            job = await _jobs.ScheduleAsync(pipeline.Id!, documents, ct).ConfigureAwait(false);
        }

        var response = new AnalysisTypeOverrideResponse
        {
            PipelineId = pipeline.Id!,
            AnalysisTypeId = pipeline.AnalysisTypeId,
            AnalysisTypeVersion = pipeline.AnalysisTypeVersion,
            JobId = job?.Id,
            Status = job?.Status.ToString() ?? "NoDocuments"
        };

        return Accepted(response);
    }

    private static async Task<Deliverable?> GetLatestDeliverableAsync(DocumentPipeline pipeline, CancellationToken ct)
    {
        var pipelineId = pipeline.Id ?? string.Empty;
        Console.WriteLine($"[Meridian] Deliverable lookup start pipeline={pipelineId} directId={pipeline.DeliverableId ?? "<null>"}");

        if (!string.IsNullOrWhiteSpace(pipeline.DeliverableId))
        {
            var direct = await Deliverable.Get(pipeline.DeliverableId, ct).ConfigureAwait(false);
            Console.WriteLine($"[Meridian] Deliverable direct get pipeline={pipelineId} found={(direct is not null)} id={pipeline.DeliverableId}");
            if (direct is not null)
            {
                return direct;
            }
        }

        Console.WriteLine($"[Meridian] Deliverable direct miss pipeline={pipelineId}; running query lookup");

        // Mongo pushes GUID-string comparisons down as native UUIDs, so equality on string fields
        // may miss matches once SmartStringGuidSerializer converts values during persistence.
        // Materialize locally when the provider returns nothing; data volume here is tiny (per-pipeline snapshots).
        var deliverables = await Deliverable.Query(d => d.PipelineId == pipelineId, ct).ConfigureAwait(false);

        Console.WriteLine($"[Meridian] Deliverable query results pipeline={pipelineId} count={deliverables.Count}");

        if (deliverables.Count == 0)
        {
            var allDeliverables = await Deliverable.All(ct).ConfigureAwait(false);
            Console.WriteLine($"[Meridian] Deliverable fallback: pipeline={pipelineId} deliverableId={pipeline.DeliverableId} allCount={allDeliverables.Count}");
            deliverables = allDeliverables
                .Where(d => string.Equals(d.PipelineId, pipelineId, StringComparison.Ordinal))
                .ToArray();
            Console.WriteLine($"[Meridian] Deliverable fallback matches={deliverables.Count}");
        }

        var latest = deliverables
            .OrderByDescending(d => d.CreatedAt)
            .FirstOrDefault();

        Console.WriteLine($"[Meridian] Deliverable lookup complete pipeline={pipelineId} selectedId={latest?.Id ?? "<null>"}");

        return latest;
    }

    private static PipelineQualitySummary? MapQuality(PipelineQualityMetrics? metrics)
    {
        if (metrics is null)
        {
            return null;
        }

        return new PipelineQualitySummary
        {
            CitationCoverage = metrics.CitationCoverage,
            HighConfidence = metrics.HighConfidence,
            MediumConfidence = metrics.MediumConfidence,
            LowConfidence = metrics.LowConfidence,
            TotalConflicts = metrics.TotalConflicts,
            AutoResolved = metrics.AutoResolved,
            ManualReviewNeeded = metrics.ManualReviewNeeded,
            NotesSourced = metrics.NotesSourced
        };
    }

    private static JToken? TryParseCanonical(string dataJson)
    {
        if (string.IsNullOrWhiteSpace(dataJson))
        {
            return null;
        }

        try
        {
            return JToken.Parse(dataJson);
        }
        catch
        {
            return null;
        }
    }

}

public sealed class AnalysisTypeOverrideRequest
{
    public string AnalysisTypeId { get; set; } = string.Empty;
    public string? Reason { get; set; }
        = null;
}

public sealed class AnalysisTypeOverrideResponse
{
    public string PipelineId { get; set; } = string.Empty;
    public string AnalysisTypeId { get; set; } = string.Empty;
    public int AnalysisTypeVersion { get; set; }
        = 1;
    public string? JobId { get; set; }
        = null;
    public string Status { get; set; } = string.Empty;
}
