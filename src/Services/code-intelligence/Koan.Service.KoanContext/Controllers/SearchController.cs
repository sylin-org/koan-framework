using System;
using System.Collections.Generic;
using System.Linq;
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
    private readonly ISearchService _retrieval;
    private readonly ProjectResolver _projectResolver;
    private readonly ILogger<SearchController> _logger;
    private readonly MetricsCollector _metricsCollector;

    public SearchController(
    ISearchService retrieval,
        ProjectResolver projectResolver,
        ILogger<SearchController> logger,
        MetricsCollector metricsCollector)
    {
        _retrieval = retrieval;
        _projectResolver = projectResolver;
        _logger = logger;
        _metricsCollector = metricsCollector;
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

    var searchStopwatch = System.Diagnostics.Stopwatch.StartNew();
        string? searchProjectId = null;

        try
        {
            // Cross-project search: If ProjectIds array provided, search multiple projects
            if (request.ProjectIds != null && request.ProjectIds.Any())
            {
                var searchResult = await SearchMultipleProjects(request, cancellationToken);
                searchStopwatch.Stop();

                // Record multi-project search metrics
                _metricsCollector.RecordMultiProjectSearch(
                    request.ProjectIds,
                    request.Query,
                    searchStopwatch.Elapsed.TotalMilliseconds,
                    0, // Result count extracted from response if needed
                    request.ProjectIds.Count,
                    searchResult is OkObjectResult);

                return searchResult;
            }

            // Single project search: Resolve project context in priority order
            string projectId;

            if (!string.IsNullOrWhiteSpace(request.ProjectId))
            {
                projectId = request.ProjectId;
            }
            else if (!string.IsNullOrWhiteSpace(request.PathContext))
            {
                var resolvedProject = await _projectResolver.ResolveProjectByPathAsync(
                    request.PathContext,
                    cancellationToken: cancellationToken);

                if (resolvedProject == null)
                {
                    return NotFound(new { error = $"Could not resolve project from pathContext: {request.PathContext}" });
                }

                projectId = resolvedProject.Id;

                _logger.LogInformation(
                    "Resolved pathContext {Path} to project {ProjectId} ({Name})",
                    request.PathContext,
                    resolvedProject.Id,
                    resolvedProject.Name);
            }
            else if (!string.IsNullOrWhiteSpace(request.LibraryId))
            {
                var resolvedProject = await _projectResolver.ResolveProjectAsync(
                    libraryId: request.LibraryId,
                    workingDirectory: null,
                    httpContext: HttpContext,
                    autoCreate: false,
                    cancellationToken: cancellationToken);

                if (resolvedProject == null)
                {
                    return NotFound(new { error = $"Project {request.LibraryId} was not found" });
                }

                projectId = resolvedProject.Id;
            }
            else if (!string.IsNullOrWhiteSpace(request.WorkingDirectory))
            {
                var resolvedProject = await _projectResolver.ResolveProjectAsync(
                    libraryId: null,
                    workingDirectory: request.WorkingDirectory,
                    httpContext: HttpContext,
                    autoCreate: false,
                    cancellationToken: cancellationToken);

                if (resolvedProject == null)
                {
                    return NotFound(new { error = $"Could not resolve project from workingDirectory: {request.WorkingDirectory}" });
                }

                projectId = resolvedProject.Id;
            }
            else
            {
                // No project specified - search all projects
                _logger.LogInformation("No project context provided - searching all projects");

                var allProjects = await Project.All(cancellationToken);
                var allProjectIds = allProjects.Select(p => p.Id).ToList();

                if (!allProjectIds.Any())
                {
                    return NotFound(new { error = "No projects found in the system" });
                }

                // Use the multi-project search flow
                var requestWithAllProjects = request with { ProjectIds = allProjectIds };
                return await SearchMultipleProjects(requestWithAllProjects, cancellationToken);
            }

            searchProjectId = projectId;

            var context = SearchRequestContext.Create(
                query: request.Query,
                projectIds: new[] { projectId },
                pathContext: request.PathContext,
                tagsAny: request.TagsAny,
                tagsAll: request.TagsAll,
                tagsExclude: request.TagsExclude,
                tagBoosts: request.TagBoosts,
                personaId: request.Persona,
                channel: SearchChannel.Web,
                continuationToken: request.ContinuationToken,
                maxTokens: request.MaxTokens,
                includeInsights: request.IncludeInsights,
                includeReasoning: request.IncludeReasoning,
                languages: request.Languages);

            var result = await _retrieval.SearchAsync(
                projectId,
                context,
                cancellationToken);

            searchStopwatch.Stop();

            // Record search metrics
            _metricsCollector.RecordSearchQuery(
                projectId,
                request.Query,
                searchStopwatch.Elapsed.TotalMilliseconds,
                result.Chunks?.Count ?? 0,
                true,
                null);

            // Get project info for consistent response structure
            var project = await Project.Get(projectId, cancellationToken);

            // Return consistent structure matching multi-project format
            return Ok(new
            {
                projects = new[] { new { id = projectId, name = project?.Name ?? "Unknown" } },
                chunks = result.Chunks,
                metadata = result.Metadata,
                sources = result.Sources,
                insights = result.Insights,
                continuationToken = result.ContinuationToken,
                warnings = result.Warnings
            });
        }
        catch (Exception ex)
        {
            searchStopwatch.Stop();

            // Record failed search
            if (searchProjectId != null)
            {
                _metricsCollector.RecordSearchQuery(
                    searchProjectId,
                    request.Query,
                    searchStopwatch.Elapsed.TotalMilliseconds,
                    0,
                    false,
                    ex.Message);
            }

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
        var pagination = HttpContext.RequestServices.GetRequiredService<Pagination>();
        ContinuationTokenData? continuationData = null;
        var chunkOffset = 0;

        if (!string.IsNullOrWhiteSpace(request.ContinuationToken))
        {
            continuationData = pagination.ParseToken(request.ContinuationToken);
            if (continuationData != null &&
                continuationData.Query == request.Query &&
                continuationData.ProjectIds != null &&
                continuationData.ProjectIds.SequenceEqual(request.ProjectIds ?? new List<string>()))
            {
                chunkOffset = continuationData.ChunkOffset;
            }
            else
            {
                continuationData = null;
            }
        }

        var aggregateTokenBudget = Math.Clamp(request.MaxTokens ?? 6000, 1000, 20000);
        var perProjectTokenBudget = Math.Max(aggregateTokenBudget, 10000);

        var allChunks = new List<object>();
        var allSources = new List<object>();
        var projects = new List<object>();
        var errors = new List<string>();

        foreach (var projectId in request.ProjectIds ?? Enumerable.Empty<string>())
        {
            try
            {
                var project = await Project.Get(projectId, cancellationToken);
                if (project == null)
                {
                    errors.Add($"Project {projectId} not found");
                    continue;
                }

                projects.Add(new { id = project.Id, name = project.Name });

                var context = SearchRequestContext.Create(
                    query: request.Query,
                    projectIds: new[] { projectId },
                    pathContext: request.PathContext,
                    tagsAny: request.TagsAny,
                    tagsAll: request.TagsAll,
                    tagsExclude: request.TagsExclude,
                    tagBoosts: request.TagBoosts,
                    personaId: request.Persona,
                    channel: SearchChannel.Web,
                    continuationToken: null,
                    maxTokens: perProjectTokenBudget,
                    includeInsights: false,
                    includeReasoning: request.IncludeReasoning,
                    languages: request.Languages);

                var result = await _retrieval.SearchAsync(projectId, context, cancellationToken);

                foreach (var chunk in result.Chunks)
                {
                    allChunks.Add(new
                    {
                        id = chunk.Id,
                        text = chunk.Text,
                        score = chunk.Score,
                        provenance = chunk.Provenance,
                        reasoning = chunk.Reasoning,
                        projectId
                    });
                }

                if (result.Sources?.Files != null)
                {
                    foreach (var file in result.Sources.Files)
                    {
                        allSources.Add(new
                        {
                            projectId,
                            projectName = project.Name,
                            filePath = file.FilePath,
                            title = file.Title,
                            commitSha = file.CommitSha
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Search failed for project {ProjectId}", projectId);
                errors.Add($"Project {projectId}: {ex.Message}");
            }
        }

        var paginatedChunks = allChunks.Skip(chunkOffset).ToList();
        var returnedChunks = new List<object>();
        var tokensReturned = 0;

        foreach (var chunk in paginatedChunks)
        {
            const int chunkTokens = 350;
            if (tokensReturned + chunkTokens > aggregateTokenBudget && returnedChunks.Count > 0)
            {
                break;
            }

            returnedChunks.Add(chunk);
            tokensReturned += chunkTokens;
        }

        var hasMoreResults = (chunkOffset + returnedChunks.Count) < allChunks.Count;
        string? continuationToken = null;

        if (hasMoreResults)
        {
            var tokenData = new ContinuationTokenData(
                ProjectId: string.Join(",", request.ProjectIds ?? new List<string>()),
                Query: request.Query,
                Alpha: 0.65f,
                TokensRemaining: aggregateTokenBudget,
                LastChunkId: string.Empty,
                CreatedAt: DateTime.UtcNow,
                Page: returnedChunks.Count == 0 ? 1 : (chunkOffset / Math.Max(returnedChunks.Count, 1)) + 1,
                ProjectIds: request.ProjectIds,
                ChunkOffset: chunkOffset + returnedChunks.Count);

            continuationToken = pagination.CreateToken(tokenData);
        }

        return Ok(new
        {
            projects,
            chunks = returnedChunks,
            metadata = new
            {
                tokensReturned,
                totalChunks = allChunks.Count,
                returnedChunks = returnedChunks.Count,
                projectCount = projects.Count,
                timestamp = DateTime.UtcNow
            },
            sources = new
            {
                totalFiles = allSources.Count,
                files = allSources
            },
            continuationToken,
            errors = errors.Count > 0 ? errors : null
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
                using (EntityContext.Partition(pid))
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
    List<string>? ProjectIds = null,
    string? PathContext = null,
    string? LibraryId = null,
    string? WorkingDirectory = null,
    string? Persona = null,
    int? MaxTokens = null,
    string? ContinuationToken = null,
    bool? IncludeInsights = null,
    bool? IncludeReasoning = null,
    List<string>? Languages = null,
    List<string>? TagsAny = null,
    List<string>? TagsAll = null,
    List<string>? TagsExclude = null,
    Dictionary<string, float>? TagBoosts = null);

/// <summary>
/// Suggestion request payload
/// </summary>
public record SuggestionRequest(
    string Prefix,
    int? MaxSuggestions = 5);
