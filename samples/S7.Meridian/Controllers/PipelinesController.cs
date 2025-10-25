using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Data.Core;
using Koan.Samples.Meridian.Infrastructure;
using Koan.Samples.Meridian.Models;
using Koan.Samples.Meridian.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Koan.Web.Controllers;

namespace Koan.Samples.Meridian.Controllers;

[ApiController]
[Route(MeridianConstants.Pipelines.Route)]
public sealed class PipelinesController : EntityController<DocumentPipeline>
{
    private readonly IPipelineBootstrapService _bootstrapService;
    private readonly IJobCoordinator _jobs;
    private readonly IRunLogWriter _runLog;

    public PipelinesController(IPipelineBootstrapService bootstrapService, IJobCoordinator jobs, IRunLogWriter runLog)
    {
        _bootstrapService = bootstrapService;
        _jobs = jobs;
        _runLog = runLog;
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
