using Koan.Data.Core;
using Koan.Samples.Meridian.Infrastructure;
using Koan.Samples.Meridian.Models;
using Koan.Samples.Meridian.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Koan.Samples.Meridian.Controllers;

/// <summary>
/// API endpoints for managing Authoritative Notes on pipelines.
/// Supports editing Notes with optional re-processing to update extractions.
/// </summary>
[ApiController]
[Route("api/pipelines/{pipelineId}/notes")]
public sealed class PipelineNotesController : ControllerBase
{
    private readonly IJobCoordinator _jobCoordinator;

    public PipelineNotesController(IJobCoordinator jobCoordinator)
    {
        _jobCoordinator = jobCoordinator;
    }

    /// <summary>
    /// Get the current Authoritative Notes for a pipeline.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(NotesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetNotes(
        [FromRoute] string pipelineId,
        CancellationToken ct)
    {
        var pipeline = await DocumentPipeline.Get(pipelineId, ct);
        if (pipeline is null)
        {
            return NotFound($"Pipeline {pipelineId} not found.");
        }

        return Ok(new NotesResponse
        {
            PipelineId = pipeline.Id,
            AuthoritativeNotes = pipeline.AuthoritativeNotes,
            UpdatedAt = pipeline.UpdatedAt
        });
    }

    /// <summary>
    /// Update Authoritative Notes for a pipeline.
    /// Optionally trigger re-processing to update field extractions.
    /// </summary>
    [HttpPut]
    [ProducesResponseType(typeof(UpdateNotesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateNotes(
        [FromRoute] string pipelineId,
        [FromBody] UpdateNotesRequest request,
        CancellationToken ct)
    {
        if (request is null)
        {
            return BadRequest("Request body is required.");
        }

        var pipeline = await DocumentPipeline.Get(pipelineId, ct);
        if (pipeline is null)
        {
            return NotFound($"Pipeline {pipelineId} not found.");
        }

        // Update the Authoritative Notes field
        var previousNotes = pipeline.AuthoritativeNotes;
        pipeline.AuthoritativeNotes = request.AuthoritativeNotes;
        pipeline.UpdatedAt = DateTime.UtcNow;
        pipeline = await pipeline.Save(ct);

        // If reProcess=true, schedule a new job to re-extract fields
        string? jobId = null;
        if (request.ReProcess)
        {
            // Get all documents for this pipeline
            var documents = await pipeline.LoadDocumentsAsync(ct);

            var documentIds = documents
                .Where(d => !d.IsVirtual)
                .Where(d => d.Status == DocumentProcessingStatus.Indexed ||
                           d.Status == DocumentProcessingStatus.Classified)
                .Select(d => d.Id)
                .ToList();

            if (documentIds.Count > 0)
            {
                var job = await _jobCoordinator.ScheduleAsync(pipelineId, documentIds, ct);
                jobId = job.Id;
            }
        }

        return Ok(new UpdateNotesResponse
        {
            PipelineId = pipeline.Id,
            AuthoritativeNotes = pipeline.AuthoritativeNotes,
            UpdatedAt = pipeline.UpdatedAt,
            ReProcessScheduled = request.ReProcess,
            JobId = jobId
        });
    }

    /// <summary>
    /// Clear Authoritative Notes for a pipeline.
    /// </summary>
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteNotes(
        [FromRoute] string pipelineId,
        [FromQuery] bool reProcess = false,
        CancellationToken ct = default)
    {
        var pipeline = await DocumentPipeline.Get(pipelineId, ct);
        if (pipeline is null)
        {
            return NotFound($"Pipeline {pipelineId} not found.");
        }

        pipeline.AuthoritativeNotes = null;
        pipeline.UpdatedAt = DateTime.UtcNow;
        pipeline = await pipeline.Save(ct);

        if (reProcess)
        {
            var documents = await pipeline.LoadDocumentsAsync(ct);

            var documentIds = documents
                .Where(d => !d.IsVirtual)
                .Where(d => d.Status == DocumentProcessingStatus.Indexed ||
                           d.Status == DocumentProcessingStatus.Classified)
                .Select(d => d.Id)
                .ToList();

            if (documentIds.Count > 0)
            {
                await _jobCoordinator.ScheduleAsync(pipelineId, documentIds, ct);
            }
        }

        return NoContent();
    }
}

/// <summary>
/// Response containing current Authoritative Notes.
/// </summary>
public sealed record NotesResponse
{
    public string PipelineId { get; init; } = string.Empty;
    public string? AuthoritativeNotes { get; init; }
    public DateTime UpdatedAt { get; init; }
}

/// <summary>
/// Request to update Authoritative Notes.
/// </summary>
public sealed record UpdateNotesRequest
{
    /// <summary>
    /// New value for Authoritative Notes (null or empty to clear).
    /// Free-text format - AI will interpret field names with fuzzy matching.
    /// </summary>
    public string? AuthoritativeNotes { get; init; }

    /// <summary>
    /// If true, schedule re-processing of the pipeline to update field extractions.
    /// If false, only update the Notes field without re-extraction.
    /// </summary>
    public bool ReProcess { get; init; } = false;
}

/// <summary>
/// Response after updating Authoritative Notes.
/// </summary>
public sealed record UpdateNotesResponse
{
    public string PipelineId { get; init; } = string.Empty;
    public string? AuthoritativeNotes { get; init; }
    public DateTime UpdatedAt { get; init; }
    public bool ReProcessScheduled { get; init; }
    public string? JobId { get; init; }
}
