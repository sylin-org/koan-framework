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
        // Parse continuation token if provided
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
                _logger.LogInformation("Multi-project continuation from offset {Offset}", chunkOffset);
            }
            else
            {
                _logger.LogWarning("Invalid multi-project continuation token, starting from beginning");
            }
        }

        // Fetch more results per project to support pagination
        var options = new SearchOptions(
            MaxTokens: 10000,  // Fetch more results per project for aggregation
            Alpha: request.Alpha ?? 0.7f,
            ContinuationToken: null,  // Don't use per-project continuation
            IncludeInsights: false,   // Only include insights on first page
            IncludeReasoning: request.IncludeReasoning ?? true,
            Languages: request.Languages);

        var allChunks = new List<object>();
        var allSources = new List<object>();
        var projects = new List<object>();
        var errors = new List<string>();

        foreach (var projectId in request.ProjectIds!)
        {
            try
            {
                // Get project info
                var project = await Project.Get(projectId, cancellationToken);
                if (project == null)
                {
                    errors.Add($"Project {projectId} not found");
                    continue;
                }

                projects.Add(new { id = project.Id, name = project.Name });

                // Search this project
                var result = await _retrieval.SearchAsync(
                    projectId,
                    request.Query,
                    options,
                    cancellationToken);

                // Add chunks with projectId tagged
                foreach (var chunk in result.Chunks)
                {
                    allChunks.Add(new
                    {
                        id = chunk.Id,
                        text = chunk.Text,
                        score = chunk.Score,
                        provenance = chunk.Provenance,
                        reasoning = chunk.Reasoning,
                        projectId = projectId  // Tag each chunk with its project
                    });
                }

                // Aggregate sources
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

        // Apply pagination: skip chunks from previous pages
        var paginatedChunks = allChunks.Skip(chunkOffset).ToList();

        // Apply token budget to determine how many chunks to return
        var maxTokens = request.TokenCounter ?? 5000;
        var returnedChunks = new List<object>();
        var tokensReturned = 0;

        foreach (var chunk in paginatedChunks)
        {
            // Estimate tokens (rough approximation)
            var chunkTokens = 350; // EstimatedTokensPerChunk from Search.cs

            if (tokensReturned + chunkTokens > maxTokens && returnedChunks.Count > 0)
            {
                break; // Token budget reached
            }

            returnedChunks.Add(chunk);
            tokensReturned += chunkTokens;
        }

        // Generate continuation token if there are more results
        var hasMoreResults = (chunkOffset + returnedChunks.Count) < allChunks.Count;
        string? continuationToken = null;

        if (hasMoreResults)
        {
            var tokenData = new ContinuationTokenData(
                ProjectId: string.Join(",", request.ProjectIds!), // Store all project IDs
                Query: request.Query,
                Alpha: request.Alpha ?? 0.7f,
                TokensRemaining: maxTokens,
                LastChunkId: string.Empty,  // Not used for multi-project
                CreatedAt: DateTime.UtcNow,
                Page: chunkOffset / returnedChunks.Count + 1,
                ProjectIds: request.ProjectIds,
                ChunkOffset: chunkOffset + returnedChunks.Count);

            continuationToken = pagination.CreateToken(tokenData);
            _logger.LogInformation("Generated multi-project continuation token: offset {NewOffset}/{Total}",
                chunkOffset + returnedChunks.Count, allChunks.Count);
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
