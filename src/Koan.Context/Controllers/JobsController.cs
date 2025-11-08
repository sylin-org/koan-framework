using Koan.Context.Models;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Koan.Context.Controllers;

/// <summary>
/// API endpoints for querying indexing job status
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class JobsController : ControllerBase
{
    private readonly ILogger<JobsController> _logger;

    public JobsController(ILogger<JobsController> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets a specific indexing job by ID
    /// </summary>
    /// <param name="jobId">The job ID (GUID)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Job status with progress tracking</returns>
    [HttpGet("{jobId}")]
    public async Task<IActionResult> GetJob(
        string jobId,
        CancellationToken cancellationToken)
    {
        try
        {
            var job = await Job.Get(jobId, cancellationToken);

            if (job == null)
            {
                return NotFound(new { error = $"Job not found: {jobId}" });
            }

            return Ok(new
            {
                job.Id,
                job.ProjectId,
                job.Status,
                job.Progress,
                job.TotalFiles,
                job.ProcessedFiles,
                job.SkippedFiles,
                job.NewFiles,
                job.ChangedFiles,
                job.ErrorFiles,
                job.ChunksCreated,
                job.VectorsSaved,
                job.StartedAt,
                job.CompletedAt,
                job.EstimatedCompletion,
                job.Elapsed,
                job.CurrentOperation,
                job.ErrorMessage
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving job {JobId}", jobId);
            return StatusCode(500, new { error = "Failed to retrieve job", details = ex.Message });
        }
    }

    /// <summary>
    /// Lists all indexing jobs for a specific project
    /// </summary>
    /// <param name="projectId">The project ID</param>
    /// <param name="limit">Maximum number of jobs to return (default: 10)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of jobs sorted by most recent first</returns>
    [HttpGet("project/{projectId}")]
    public async Task<IActionResult> ListJobsByProject(
        string projectId,
        [FromQuery] int limit = 10,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var jobs = await Job.Query(
                j => j.ProjectId == projectId,
                cancellationToken);

            var jobsList = jobs
                .OrderByDescending(j => j.StartedAt)
                .Take(limit)
                .Select(j => new
                {
                    j.Id,
                    j.ProjectId,
                    j.Status,
                    j.Progress,
                    j.TotalFiles,
                    j.ProcessedFiles,
                    j.ChunksCreated,
                    j.VectorsSaved,
                    j.StartedAt,
                    j.CompletedAt,
                    j.Elapsed,
                    j.CurrentOperation,
                    HasErrors = j.ErrorFiles > 0
                })
                .ToList();

            return Ok(new
            {
                ProjectId = projectId,
                TotalJobs = jobsList.Count,
                Jobs = jobsList
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing jobs for project {ProjectId}", projectId);
            return StatusCode(500, new { error = "Failed to list jobs", details = ex.Message });
        }
    }

    /// <summary>
    /// Gets the current/latest indexing job for a project
    /// </summary>
    /// <param name="projectId">The project ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Most recent job for the project</returns>
    [HttpGet("project/{projectId}/current")]
    public async Task<IActionResult> GetCurrentJob(
        string projectId,
        CancellationToken cancellationToken)
    {
        try
        {
            var jobs = await Job.Query(
                j => j.ProjectId == projectId,
                cancellationToken);

            var currentJob = jobs
                .OrderByDescending(j => j.StartedAt)
                .FirstOrDefault();

            if (currentJob == null)
            {
                return NotFound(new { error = $"No jobs found for project: {projectId}" });
            }

            return Ok(new
            {
                currentJob.Id,
                currentJob.ProjectId,
                currentJob.Status,
                currentJob.Progress,
                currentJob.TotalFiles,
                currentJob.ProcessedFiles,
                currentJob.SkippedFiles,
                currentJob.NewFiles,
                currentJob.ChangedFiles,
                currentJob.ErrorFiles,
                currentJob.ChunksCreated,
                currentJob.VectorsSaved,
                currentJob.StartedAt,
                currentJob.CompletedAt,
                currentJob.EstimatedCompletion,
                currentJob.Elapsed,
                currentJob.CurrentOperation,
                currentJob.ErrorMessage,
                IsActive = currentJob.Status == JobStatus.Planning ||
                          currentJob.Status == JobStatus.Indexing ||
                          currentJob.Status == JobStatus.Pending
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving current job for project {ProjectId}", projectId);
            return StatusCode(500, new { error = "Failed to retrieve current job", details = ex.Message });
        }
    }

    /// <summary>
    /// Lists all active (in-progress) indexing jobs across all projects
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of active jobs</returns>
    [HttpGet("active")]
    public async Task<IActionResult> ListActiveJobs(CancellationToken cancellationToken)
    {
        try
        {
            var jobs = await Job.Query(
                j => j.Status == JobStatus.Planning ||
                     j.Status == JobStatus.Indexing ||
                     j.Status == JobStatus.Pending,
                cancellationToken);

            var jobsList = jobs
                .OrderByDescending(j => j.StartedAt)
                .Select(j => new
                {
                    j.Id,
                    j.ProjectId,
                    j.Status,
                    j.Progress,
                    j.TotalFiles,
                    j.ProcessedFiles,
                    j.ChunksCreated,
                    j.StartedAt,
                    j.EstimatedCompletion,
                    j.Elapsed,
                    j.CurrentOperation
                })
                .ToList();

            return Ok(new
            {
                Count = jobsList.Count,
                Jobs = jobsList
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing active jobs");
            return StatusCode(500, new { error = "Failed to list active jobs", details = ex.Message });
        }
    }

    /// <summary>
    /// Cancels an in-progress indexing job
    /// </summary>
    /// <param name="jobId">The job ID to cancel</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Updated job status</returns>
    [HttpPost("{jobId}/cancel")]
    public async Task<IActionResult> CancelJob(
        string jobId,
        CancellationToken cancellationToken)
    {
        try
        {
            var job = await Job.Get(jobId, cancellationToken);

            if (job == null)
            {
                return NotFound(new { error = $"Job not found: {jobId}" });
            }

            // Only allow cancellation of active jobs
            if (job.Status != JobStatus.Planning &&
                job.Status != JobStatus.Indexing &&
                job.Status != JobStatus.Pending)
            {
                return BadRequest(new
                {
                    error = "Job cannot be cancelled",
                    reason = $"Job is already in terminal state: {job.Status}"
                });
            }

            job.Cancel();
            await job.Save(cancellationToken);

            _logger.LogInformation("Job {JobId} cancelled for project {ProjectId}", jobId, job.ProjectId);

            return Ok(new
            {
                job.Id,
                job.Status,
                Message = "Job cancelled successfully"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling job {JobId}", jobId);
            return StatusCode(500, new { error = "Failed to cancel job", details = ex.Message });
        }
    }
}
