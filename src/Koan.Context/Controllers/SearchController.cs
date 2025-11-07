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
            // Resolve project context in priority order: explicit ID -> pathContext -> libraryId
            string projectId;

            if (!string.IsNullOrWhiteSpace(request.ProjectId))
            {
                projectId = request.ProjectId;
            }
            else if (!string.IsNullOrWhiteSpace(request.PathContext))
            {
                var project = await _projectResolver.ResolveProjectByPathAsync(
                    request.PathContext,
                    cancellationToken: cancellationToken);

                if (project == null)
                {
                    return NotFound(new { error = $"Could not resolve project from pathContext: {request.PathContext}" });
                }

                projectId = project.Id;

                _logger.LogInformation(
                    "Resolved pathContext {Path} to project {ProjectId} ({Name})",
                    request.PathContext,
                    project.Id,
                    project.Name);
            }
            else if (!string.IsNullOrWhiteSpace(request.LibraryId))
            {
                var project = await _projectResolver.ResolveProjectAsync(
                    libraryId: request.LibraryId,
                    workingDirectory: null,
                    httpContext: HttpContext,
                    autoCreate: false,
                    cancellationToken: cancellationToken);

                if (project == null)
                {
                    return NotFound(new { error = $"Project {request.LibraryId} was not found" });
                }

                projectId = project.Id;
            }
            else if (!string.IsNullOrWhiteSpace(request.WorkingDirectory))
            {
                var project = await _projectResolver.ResolveProjectAsync(
                    libraryId: null,
                    workingDirectory: request.WorkingDirectory,
                    httpContext: HttpContext,
                    autoCreate: false,
                    cancellationToken: cancellationToken);

                if (project == null)
                {
                    return NotFound(new { error = $"Could not resolve project from workingDirectory: {request.WorkingDirectory}" });
                }

                projectId = project.Id;
            }
            else
            {
                return BadRequest(new { error = "Must provide projectId, pathContext, libraryId, or workingDirectory" });
            }

            var options = new SearchOptions(
                MaxTokens: request.Tokens ?? 5000,
                Alpha: request.Alpha ?? 0.7f,
                ContinuationToken: request.ContinuationToken,
                IncludeInsights: request.IncludeInsights ?? true,
                IncludeReasoning: request.IncludeReasoning ?? true);

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
    string? PathContext = null,
    string? LibraryId = null,
    string? WorkingDirectory = null,
    float? Alpha = null,
    int? Tokens = null,
    string? ContinuationToken = null,
    bool? IncludeInsights = null,
    bool? IncludeReasoning = null);
