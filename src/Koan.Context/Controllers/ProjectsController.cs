using Koan.Context.Models;
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
/// </remarks>
[ApiController]
[Route("api/projects")]
public class ProjectsController : EntityController<Project, Guid>
{
    // EntityController provides all base CRUD operations automatically

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

            var saved = await Project.UpsertAsync(project);

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
    public async Task<ActionResult<Project>> MarkIndexed(Guid id, [FromBody] IndexMetadataRequest request)
    {
        var project = await Project.Get(id);
        if (project == null)
        {
            return NotFound();
        }

        project.MarkIndexed(request.DocumentCount, request.IndexedBytes);
        var updated = await Project.UpsertAsync(project);

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
