using Koan.Context.Models;
using Koan.Context.Services;
using Koan.Data.Core;
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
    private readonly Search _retrieval;
    private readonly ProjectResolver _projectResolver;
    private readonly ILogger<SearchController> _logger;

    public SearchController(
        Search retrieval,
        ProjectResolver projectResolver,
        ILogger<SearchController> logger)
    {
        _retrieval = retrieval;
        _projectResolver = projectResolver;
        _logger = logger;
    }

    /// <summary>
    /// Performs semantic search within a project or across multiple projects
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
            // Cross-project search: If ProjectIds array provided, search multiple projects
            if (request.ProjectIds != null && request.ProjectIds.Any())
            {
                return await SearchMultipleProjects(request, cancellationToken);
            }

            // Single project search: Resolve project context in priority order
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
                return BadRequest(new { error = "Must provide projectId, projectIds, pathContext, libraryId, or workingDirectory" });
            }

            var options = new SearchOptions(
                MaxTokens: request.TokenCounter ?? 5000,
                Alpha: request.Alpha ?? 0.7f,
                ContinuationToken: request.ContinuationToken,
                IncludeInsights: request.IncludeInsights ?? true,
                IncludeReasoning: request.IncludeReasoning ?? true,
                Languages: request.Languages);

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

    /// <summary>
    /// Search across multiple projects and aggregate results
    /// </summary>
    private async Task<IActionResult> SearchMultipleProjects(
        SearchRequest request,
        CancellationToken cancellationToken)
    {
        var options = new SearchOptions(
            MaxTokens: request.TokenCounter ?? 5000,
            Alpha: request.Alpha ?? 0.7f,
            ContinuationToken: request.ContinuationToken,
            IncludeInsights: request.IncludeInsights ?? true,
            IncludeReasoning: request.IncludeReasoning ?? true,
            Languages: request.Languages);

        var projectResults = new List<object>();
        var errors = new List<object>();

        foreach (var projectId in request.ProjectIds!)
        {
            try
            {
                var result = await _retrieval.SearchAsync(
                    projectId,
                    request.Query,
                    options,
                    cancellationToken);

                projectResults.Add(new
                {
                    projectId,
                    results = result
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Search failed for project {ProjectId}", projectId);
                errors.Add(new
                {
                    projectId,
                    error = ex.Message
                });
            }
        }

        return Ok(new
        {
            query = request.Query,
            totalProjects = request.ProjectIds.Count,
            successfulProjects = projectResults.Count,
            failedProjects = errors.Count,
            results = projectResults,
            errors
        });
    }

    /// <summary>
    /// Get available languages/file types for a project or across projects
    /// </summary>
    /// <param name="projectId">Optional project ID</param>
    /// <param name="projectIds">Optional comma-separated project IDs for multi-project stats</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Language statistics with document counts</returns>
    [HttpGet("languages")]
    public async Task<IActionResult> GetLanguages(
        [FromQuery] string? projectId = null,
        [FromQuery] string? projectIds = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var projectIdList = new List<string>();

            if (!string.IsNullOrWhiteSpace(projectIds))
            {
                projectIdList.AddRange(projectIds.Split(',', StringSplitOptions.RemoveEmptyEntries));
            }
            else if (!string.IsNullOrWhiteSpace(projectId))
            {
                projectIdList.Add(projectId);
            }
            else
            {
                // Get all projects if none specified
                var allProjects = await Project.All(cancellationToken);
                projectIdList.AddRange(allProjects.Select(p => p.Id));
            }

            var languageStats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var totalChunks = 0;

            foreach (var pid in projectIdList)
            {
                var partitionId = $"proj-{Guid.Parse(pid):N}";

                using (EntityContext.Partition(partitionId))
                {
                    var chunks = await Chunk.All(cancellationToken);

                    foreach (var chunk in chunks)
                    {
                        totalChunks++;
                        var lang = string.IsNullOrWhiteSpace(chunk.Language) ? "other" : chunk.Language;

                        if (languageStats.TryGetValue(lang, out var count))
                        {
                            languageStats[lang] = count + 1;
                        }
                        else
                        {
                            languageStats[lang] = 1;
                        }
                    }
                }
            }

            // Sort by count descending
            var sorted = languageStats
                .OrderByDescending(kvp => kvp.Value)
                .Select(kvp => new
                {
                    language = kvp.Key,
                    count = kvp.Value,
                    percentage = totalChunks > 0 ? (kvp.Value * 100.0 / totalChunks) : 0.0
                })
                .ToList();

            return Ok(new
            {
                totalChunks,
                languages = sorted
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get language statistics");
            return StatusCode(500, new { error = "Failed to get language statistics", message = ex.Message });
        }
    }

    /// <summary>
    /// Get search suggestions based on query prefix
    /// </summary>
    /// <param name="request">Suggestion request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Suggested search queries</returns>
    [HttpPost("suggestions")]
    public async Task<IActionResult> GetSuggestions(
        [FromBody] SuggestionRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Prefix))
        {
            return BadRequest(new { error = "Prefix cannot be empty" });
        }

        try
        {
            // Simple suggestion logic: return common query patterns based on prefix
            var suggestions = new List<string>();

            // Pattern-based suggestions
            if (request.Prefix.Length >= 2)
            {
                // Add code-specific suggestions
                if (request.Prefix.Contains("class") || request.Prefix.Contains("interface"))
                {
                    suggestions.Add($"{request.Prefix} definition");
                    suggestions.Add($"{request.Prefix} implementation");
                }
                else if (request.Prefix.Contains("function") || request.Prefix.Contains("method"))
                {
                    suggestions.Add($"{request.Prefix} signature");
                    suggestions.Add($"{request.Prefix} usage example");
                }
                else
                {
                    suggestions.Add($"{request.Prefix} documentation");
                    suggestions.Add($"{request.Prefix} examples");
                    suggestions.Add($"{request.Prefix} best practices");
                }
            }

            // Limit to max suggestions
            var limit = request.MaxSuggestions ?? 5;
            suggestions = suggestions.Take(limit).ToList();

            return Ok(new
            {
                prefix = request.Prefix,
                suggestions
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Suggestion generation failed for prefix {Prefix}", request.Prefix);
            return StatusCode(500, new { error = "Suggestion generation failed", message = ex.Message });
        }
    }
}

/// <summary>
/// Search request payload
/// </summary>
public record SearchRequest(
    string Query,
    string? ProjectId = null,
    List<string>? ProjectIds = null,  // For cross-project search
    string? PathContext = null,
    string? LibraryId = null,
    string? WorkingDirectory = null,
    float? Alpha = null,
    int? TokenCounter = null,
    string? ContinuationToken = null,
    bool? IncludeInsights = null,
    bool? IncludeReasoning = null,
    List<string>? Languages = null);  // Filter by programming language/file type

/// <summary>
/// Suggestion request payload
/// </summary>
public record SuggestionRequest(
    string Prefix,
    int? MaxSuggestions = 5);
