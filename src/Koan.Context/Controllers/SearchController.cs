using Koan.Context.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Koan.Context.Controllers;

/// <summary>
/// API endpoints for semantic search
/// </summary>
[ApiController]
[Route("api/search")]
public class SearchController : ControllerBase
{
    private readonly IRetrievalService _retrieval;
    private readonly ProjectResolver _projectResolver;
    private readonly ILogger<SearchController> _logger;

    public SearchController(
        IRetrievalService retrieval,
        ProjectResolver projectResolver,
        ILogger<SearchController> logger)
    {
        _retrieval = retrieval;
        _projectResolver = projectResolver;
        _logger = logger;
    }

    /// <summary>
    /// Performs semantic search within a project
    /// </summary>
    /// <param name="request">Search request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Search results with provenance</returns>
    [HttpPost]
    public async Task<IActionResult> Search(
        [FromBody] SearchRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return BadRequest(new { error = "Query cannot be empty" });
        }

        try
        {
            // Resolve project from explicit ID or working directory
            string projectId;

            if (!string.IsNullOrWhiteSpace(request.ProjectId))
            {
                // Use explicit project ID
                projectId = request.ProjectId;
            }
            else if (!string.IsNullOrWhiteSpace(request.WorkingDirectory))
            {
                // Resolve project from working directory (auto-creates and indexes if needed)
                var project = await _projectResolver.ResolveProjectAsync(
                    libraryId: null,
                    workingDirectory: request.WorkingDirectory,
                    autoCreate: true,
                    cancellationToken: cancellationToken);

                if (project == null)
                {
                    return NotFound(new { error = $"Could not resolve project from directory: {request.WorkingDirectory}" });
                }

                projectId = project.Id;

                _logger.LogInformation(
                    "Resolved project {ProjectId} ({ProjectName}) from working directory {WorkingDirectory}",
                    projectId,
                    project.Name,
                    request.WorkingDirectory);
            }
            else
            {
                return BadRequest(new { error = "Either ProjectId or WorkingDirectory must be provided" });
            }

            var options = new SearchOptions(
                Alpha: request.Alpha ?? 0.7f,
                TopK: request.TopK ?? 10);

            var result = await _retrieval.SearchAsync(
                projectId,
                request.Query,
                options,
                cancellationToken);

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Search failed for query {Query}", request.Query);
            return StatusCode(500, new { error = "Search failed", message = ex.Message });
        }
    }
}

/// <summary>
/// Search request payload
/// </summary>
public record SearchRequest(
    string Query,
    string? ProjectId = null,
    string? WorkingDirectory = null,
    float? Alpha = null,
    int? TopK = null);
