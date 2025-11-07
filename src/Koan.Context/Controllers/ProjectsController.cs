using Koan.Context.Models;
using Koan.Context.Services;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Web.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace Koan.Context.Controllers;

/// <summary>
/// REST API controller for managing code projects
/// </summary>
/// <remarks>
/// Provides full CRUD operations via EntityController base class:
/// - GET /api/projects - List all projects with filtering/paging
/// - GET /api/projects/{id} - Get single project by ID
/// - POST /api/projects - Create new project
/// - PUT /api/projects/{id} - Update existing project
/// - PATCH /api/projects/{id} - Partial update via JSON Patch
/// - DELETE /api/projects/{id} - Delete project
/// Additional endpoints:
/// - POST /api/projects/create - Create with validation
/// - POST /api/projects/{id}/indexed - Mark as indexed
/// - POST /api/projects/{id}/index - Trigger indexing
/// - GET /api/projects/active - List active projects
/// </remarks>
[ApiController]
[Route("api/projects")]
public class ProjectsController : EntityController<Project>
{
    private readonly IIndexingService _indexingService;
    private readonly FileMonitoringService _fileMonitoring;

    public ProjectsController(
        IIndexingService indexingService,
        FileMonitoringService fileMonitoring)
    {
        _indexingService = indexingService ?? throw new ArgumentNullException(nameof(indexingService));
        _fileMonitoring = fileMonitoring ?? throw new ArgumentNullException(nameof(fileMonitoring));
    }

    /// <summary>
    /// Create a new project from minimal data
    /// </summary>
    /// <param name="request">Project creation request</param>
    /// <returns>Created project with auto-generated ID</returns>
    [HttpPost("create")]
    public async Task<ActionResult<Project>> CreateProject([FromBody] CreateProjectRequest request)
    {
        try
        {
            var project = Project.Create(request.Name, request.RootPath, request.ProjectType);

            if (!string.IsNullOrWhiteSpace(request.GitRemote))
            {
                project.GitRemote = request.GitRemote;
            }

            var saved = await project.Save();

            return CreatedAtAction(nameof(GetById), new { id = saved.Id }, saved);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Mark a project as indexed with metadata
    /// </summary>
    /// <param name="id">Project ID</param>
    /// <param name="request">Index metadata</param>
    /// <returns>Updated project</returns>
    [HttpPost("{id}/indexed")]
    public async Task<ActionResult<Project>> MarkIndexed(string id, [FromBody] IndexMetadataRequest request)
    {
        var project = await Project.Get(id);
        if (project == null)
        {
            return NotFound();
        }

        project.MarkIndexed(request.DocumentCount, request.IndexedBytes);
        var updated = await project.Save();

        return Ok(updated);
    }

    /// <summary>
    /// Get active projects only
    /// </summary>
    /// <returns>List of active projects</returns>
    [HttpGet("active")]
    public async Task<ActionResult<IEnumerable<Project>>> GetActive()
    {
        var projects = await Project.Query(p => p.IsActive);
        return Ok(projects);
    }

    /// <summary>
    /// Trigger indexing for a project
    /// </summary>
    /// <param name="id">Project ID</param>
    /// <param name="force">If true, cancels any existing indexing job and starts a new one</param>
    /// <returns>Indexing result</returns>
    [HttpPost("{id}/index")]
    public async Task<IActionResult> IndexProject(string id, [FromQuery] bool force = false)
    {
        var project = await Project.Get(id);
        if (project == null)
        {
            return NotFound();
        }

        // Check if already indexing and user didn't specify force
        if (project.Status == IndexingStatus.Indexing && !force)
        {
            return Conflict(new
            {
                status = "conflict",
                message = $"Project '{project.Name}' is already being indexed. " +
                          "To cancel the current job and restart, add ?force=true to your request.",
                projectId = project.Id,
                projectName = project.Name,
                statusUrl = $"/api/projects/{id}/status",
                hint = "Use POST /api/projects/{id}/index?force=true to force restart indexing"
            });
        }

        // Start indexing in background (fire-and-forget)
        _ = Task.Run(async () =>
        {
            try
            {
                await _indexingService.IndexProjectAsync(id, force: force);
            }
            catch
            {
                // Errors are logged by IndexingService
            }
        });

        // Return 202 Accepted immediately
        return Accepted(new
        {
            message = force ? "Indexing started (force restart)" : "Indexing started",
            projectId = id,
            statusUrl = $"/api/projects/{id}/status",
            force = force
        });
    }

    /// <summary>
    /// Get project indexing status
    /// </summary>
    /// <param name="id">Project ID</param>
    /// <returns>Project status information including active job details</returns>
    [HttpGet("{id}/status")]
    public async Task<IActionResult> GetProjectStatus(string id)
    {
        var project = await Project.Get(id);
        if (project == null)
            return NotFound();

        // Fetch active job details if there's an active job
        IndexingJob? activeJob = null;
        if (!string.IsNullOrWhiteSpace(project.ActiveJobId))
        {
            activeJob = await IndexingJob.Get(project.ActiveJobId);
        }

        return Ok(new
        {
            projectId = project.Id,
            name = project.Name,
            status = project.Status.ToString(),
            lastIndexed = project.LastIndexed,
            documentCount = project.DocumentCount,
            indexingStartedAt = project.IndexingStartedAt,
            indexingCompletedAt = project.IndexingCompletedAt,
            isMonitoringEnabled = project.IsMonitoringEnabled,
            monitorCodeChanges = project.MonitorCodeChanges,
            monitorDocChanges = project.MonitorDocChanges,
            error = project.IndexingError,
            activeJob = activeJob != null ? new
            {
                id = activeJob.Id,
                status = activeJob.Status.ToString(),
                progress = activeJob.Progress,
                totalFiles = activeJob.TotalFiles,
                processedFiles = activeJob.ProcessedFiles,
                chunksCreated = activeJob.ChunksCreated,
                vectorsSaved = activeJob.VectorsSaved,
                startedAt = activeJob.StartedAt,
                estimatedCompletion = activeJob.EstimatedCompletion,
                elapsed = activeJob.Elapsed,
                currentOperation = activeJob.CurrentOperation
            } : null
        });
    }

    /// <summary>
    /// Update project monitoring settings
    /// </summary>
    /// <param name="id">Project ID</param>
    /// <param name="request">Monitoring settings update</param>
    /// <returns>Updated settings confirmation</returns>
    [HttpPatch("{id}/monitoring")]
    public async Task<IActionResult> UpdateMonitoringSettings(
        string id,
        [FromBody] UpdateMonitoringRequest request)
    {
        var project = await Project.Get(id);
        if (project == null)
            return NotFound();

        // Update settings
        if (request.MonitorCodeChanges.HasValue)
            project.MonitorCodeChanges = request.MonitorCodeChanges.Value;

        if (request.MonitorDocChanges.HasValue)
            project.MonitorDocChanges = request.MonitorDocChanges.Value;

        await project.Save();

        // Restart file watcher if monitoring was re-enabled
        if (project.IsMonitoringEnabled)
        {
            await _fileMonitoring.StartWatchingProjectAsync(project);
        }
        else
        {
            await _fileMonitoring.StopWatchingProjectAsync(project.Id);
        }

        return Ok(new { message = "Monitoring settings updated", project });
    }

    /// <summary>
    /// Manually trigger project re-indexing
    /// </summary>
    /// <param name="id">Project ID</param>
    /// <param name="force">If true, cancels any existing indexing job and starts a new one</param>
    /// <returns>Indexing started confirmation</returns>
    [HttpPost("{id}/reindex")]
    public async Task<IActionResult> ManualReindex(string id, [FromQuery] bool force = false)
    {
        var project = await Project.Get(id);
        if (project == null)
            return NotFound();

        // Check if already indexing and user didn't specify force
        if (project.Status == IndexingStatus.Indexing && !force)
        {
            return Conflict(new
            {
                status = "conflict",
                message = $"Project '{project.Name}' is already being indexed. " +
                          "To cancel the current job and restart, add ?force=true to your request.",
                projectId = project.Id,
                projectName = project.Name,
                statusUrl = $"/api/projects/{id}/status",
                hint = "Use POST /api/projects/{id}/reindex?force=true to force restart indexing"
            });
        }

        // Manual reindex works even if monitoring disabled
        project.Status = IndexingStatus.Indexing;
        project.IndexingStartedAt = DateTime.UtcNow;
        await project.Save();

        _ = Task.Run(async () =>
        {
            try
            {
                await _indexingService.IndexProjectAsync(id, cancellationToken: CancellationToken.None, force: force);
            }
            catch (Exception)
            {
                // Error will be recorded in project.IndexingError
            }
        });

        return Accepted(new
        {
            message = force ? "Reindexing started (force restart)" : "Reindexing started",
            projectId = project.Id,
            statusUrl = $"/api/projects/{id}/status",
            force = force
        });
    }
}

/// <summary>
/// Request model for creating a new project
/// </summary>
public record CreateProjectRequest(
    string Name,
    string RootPath,
    ProjectType ProjectType = ProjectType.Unknown,
    string? GitRemote = null
);

/// <summary>
/// Request model for updating index metadata
/// </summary>
public record IndexMetadataRequest(
    int DocumentCount,
    long IndexedBytes
);

/// <summary>
/// Request model for updating monitoring settings
/// </summary>
public record UpdateMonitoringRequest(
    bool? MonitorCodeChanges,
    bool? MonitorDocChanges
);
