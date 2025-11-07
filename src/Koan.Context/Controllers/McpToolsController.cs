using Koan.Context.Models;
using Koan.Context.Services;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Koan.Context.Controllers;

/// <summary>
/// Custom MCP tools for Koan Context semantic search integration
/// </summary>
[ApiController]
[Route("api/mcp")]
public class McpToolsController : ControllerBase
{
    private readonly IRetrievalService _retrieval;
    private readonly IIndexingService _indexing;
    private readonly ProjectResolver _projectResolver;
    private readonly ILogger<McpToolsController> _logger;

    public McpToolsController(
        IRetrievalService retrieval,
        IIndexingService indexing,
        ProjectResolver projectResolver,
        ILogger<McpToolsController> logger)
    {
        _retrieval = retrieval;
        _indexing = indexing;
        _projectResolver = projectResolver;
        _logger = logger;
    }

    /// <summary>
    /// MCP Tool: context.resolve_library_id
    /// Resolves a fuzzy library/project name to a specific project ID
    /// </summary>
    /// <param name="request">Resolve request with library name</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Matching projects with similarity scores</returns>
    [HttpPost("resolve-library-id")]
    public async Task<IActionResult> ResolveLibraryId(
        [FromBody] ResolveLibraryIdRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.LibraryName))
        {
            return BadRequest(new { error = "LibraryName cannot be empty" });
        }

        try
        {
            _logger.LogInformation(
                "Resolving library name: {LibraryName}",
                request.LibraryName);

            // Query all projects
            var projects = await Project.Query(_ => true, cancellationToken);

            // Fuzzy match against project names
            var matches = projects
                .Select(p => new
                {
                    Project = p,
                    Score = CalculateFuzzyScore(request.LibraryName, p.Name)
                })
                .Where(m => m.Score > 0.3) // Threshold for relevance
                .OrderByDescending(m => m.Score)
                .Take(request.MaxResults ?? 5)
                .Select(m => new LibraryMatch(
                    Id: m.Project.Id,
                    Name: m.Project.Name,
                    RootPath: m.Project.RootPath,
                    DocumentCount: m.Project.DocumentCount,
                    Score: m.Score,
                    IsActive: m.Project.IsActive
                ))
                .ToList();

            _logger.LogInformation(
                "Found {MatchCount} matches for library: {LibraryName}",
                matches.Count,
                request.LibraryName);

            var response = new ResolveLibraryIdResponse(
                Query: request.LibraryName,
                Matches: matches,
                TotalMatches: matches.Count);

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve library name: {LibraryName}", request.LibraryName);
            return StatusCode(500, new { error = "Failed to resolve library", message = ex.Message });
        }
    }

    /// <summary>
    /// MCP Tool: context.get_library_docs
    /// Performs semantic search within a specific library/project with pagination
    /// Supports auto-creation and async indexing workflow
    /// </summary>
    /// <param name="request">Search request with library ID and query</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Search results or 202 Accepted if indexing</returns>
    [HttpPost("get-library-docs")]
    public async Task<IActionResult> GetLibraryDocs(
        [FromBody] GetLibraryDocsRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return BadRequest(new { error = "Query cannot be empty" });
        }

        try
        {
            Project? project = null;

            if (!string.IsNullOrWhiteSpace(request.LibraryId))
            {
                project = await _projectResolver.ResolveProjectAsync(
                    request.LibraryId,
                    workingDirectory: null,
                    httpContext: HttpContext,
                    autoCreate: true,
                    cancellationToken);
            }
            else if (!string.IsNullOrWhiteSpace(request.PathContext))
            {
                project = await _projectResolver.ResolveProjectByPathAsync(
                    request.PathContext,
                    autoCreate: true,
                    cancellationToken);
            }
            else if (!string.IsNullOrWhiteSpace(request.WorkingDirectory))
            {
                project = await _projectResolver.ResolveProjectAsync(
                    libraryId: null,
                    workingDirectory: request.WorkingDirectory,
                    httpContext: HttpContext,
                    autoCreate: true,
                    cancellationToken);
            }

            if (project == null)
            {
                return BadRequest(new
                {
                    error = "Could not resolve project from context",
                    hint = "Provide either libraryId, pathContext, or workingDirectory"
                });
            }

            // Check indexing status
            switch (project.Status)
            {
                case IndexingStatus.NotIndexed:
                    // Start indexing in background
                    project.Status = IndexingStatus.Indexing;
                    project.IndexingStartedAt = DateTime.UtcNow;
                    await project.Save(cancellationToken);

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await _indexing.IndexProjectAsync(project.Id, cancellationToken: CancellationToken.None);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Background indexing failed for project {ProjectId}", project.Id);
                        }
                    }, CancellationToken.None);

                    return Accepted(new
                    {
                        status = "indexing",
                        message = $"Project '{project.Name}' is being indexed. Please try again in a few minutes.",
                        projectId = project.Id,
                        projectName = project.Name,
                        estimatedDuration = "2-5 minutes",
                        statusUrl = $"/api/projects/{project.Id}/status",
                        retryAfter = 120
                    });

                case IndexingStatus.Indexing:
                    // Already indexing - return 409 Conflict
                    return Conflict(new
                    {
                        status = "conflict",
                        message = $"Project '{project.Name}' is already being indexed. " +
                                  "To cancel the current job and restart, use the /api/projects/{project.Id}/reindex endpoint with force=true parameter.",
                        projectId = project.Id,
                        projectName = project.Name,
                        statusUrl = $"/api/projects/{project.Id}/status",
                        hint = "Use POST /api/projects/{project.Id}/reindex?force=true to force restart indexing"
                    });

                case IndexingStatus.Updating:
                    // Project is updating but still queryable - proceed with search
                    _logger.LogDebug("Project {Name} is updating, but proceeding with query", project.Name);
                    break;

                case IndexingStatus.Failed:
                    return StatusCode(500, new
                    {
                        status = "failed",
                        message = $"Project '{project.Name}' indexing failed",
                        error = project.IndexingError,
                        projectId = project.Id
                    });

                case IndexingStatus.Ready:
                    // Proceed with search
                    break;
            }

            _logger.LogInformation(
                "Searching library {LibraryName} ({LibraryId}) for: {Query}",
                project.Name,
                project.Id,
                request.Query);

            // Perform semantic search
            var searchOptions = new SearchOptions(
                MaxTokens: request.Tokens ?? 5000,
                Alpha: request.Alpha ?? 0.7f,
                ContinuationToken: request.ContinuationToken,
                IncludeInsights: request.IncludeInsights ?? true,
                IncludeReasoning: request.IncludeReasoning ?? true);

            var result = await _retrieval.SearchAsync(
                project.Id,
                request.Query,
                searchOptions,
                cancellationToken);

            _logger.LogInformation(
                "Found {ResultCount} results in library {LibraryName}",
                result.Chunks.Count,
                project.Name);

            var response = new GetLibraryDocsResponse(
                LibraryId: project.Id,
                LibraryName: project.Name,
                Query: request.Query,
                Result: result,
                HasMore: !string.IsNullOrWhiteSpace(result.ContinuationToken),
                IndexingStatus: project.Status.ToString());

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to search library for query: {Query}",
                request.Query);
            return StatusCode(500, new { error = "Search failed", message = ex.Message });
        }
    }

    /// <summary>
    /// Calculates fuzzy matching score between query and target string
    /// Uses Levenshtein distance normalized to 0.0-1.0 range
    /// </summary>
    private static double CalculateFuzzyScore(string query, string target)
    {
        if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(target))
        {
            return 0.0;
        }

        var queryLower = query.ToLowerInvariant();
        var targetLower = target.ToLowerInvariant();

        // Exact match
        if (queryLower == targetLower)
        {
            return 1.0;
        }

        // Contains match
        if (targetLower.Contains(queryLower))
        {
            return 0.8 + (0.2 * (1.0 - (double)(targetLower.Length - queryLower.Length) / targetLower.Length));
        }

        // Starts with match
        if (targetLower.StartsWith(queryLower))
        {
            return 0.7 + (0.2 * ((double)queryLower.Length / targetLower.Length));
        }

        // Levenshtein distance
        var distance = CalculateLevenshteinDistance(queryLower, targetLower);
        var maxLength = Math.Max(queryLower.Length, targetLower.Length);
        var similarity = 1.0 - ((double)distance / maxLength);

        return similarity;
    }

    /// <summary>
    /// Calculates Levenshtein distance between two strings
    /// </summary>
    private static int CalculateLevenshteinDistance(string source, string target)
    {
        if (string.IsNullOrEmpty(source))
        {
            return target?.Length ?? 0;
        }

        if (string.IsNullOrEmpty(target))
        {
            return source.Length;
        }

        var sourceLength = source.Length;
        var targetLength = target.Length;
        var matrix = new int[sourceLength + 1, targetLength + 1];

        for (var i = 0; i <= sourceLength; i++)
        {
            matrix[i, 0] = i;
        }

        for (var j = 0; j <= targetLength; j++)
        {
            matrix[0, j] = j;
        }

        for (var i = 1; i <= sourceLength; i++)
        {
            for (var j = 1; j <= targetLength; j++)
            {
                var cost = target[j - 1] == source[i - 1] ? 0 : 1;
                matrix[i, j] = Math.Min(
                    Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                    matrix[i - 1, j - 1] + cost);
            }
        }

        return matrix[sourceLength, targetLength];
    }
}

/// <summary>
/// Request for resolving fuzzy library name to project ID
/// </summary>
public record ResolveLibraryIdRequest(
    string LibraryName,
    int? MaxResults = null);

/// <summary>
/// Response with matched libraries
/// </summary>
public record ResolveLibraryIdResponse(
    string Query,
    IReadOnlyList<LibraryMatch> Matches,
    int TotalMatches);

/// <summary>
/// Individual library match with score
/// </summary>
public record LibraryMatch(
    string Id,
    string Name,
    string RootPath,
    int DocumentCount,
    double Score,
    bool IsActive);

/// <summary>
/// Request for semantic search within a library
/// </summary>
public record GetLibraryDocsRequest(
    string Query,
    string? LibraryId = null,
    string? PathContext = null,
    string? WorkingDirectory = null,
    float? Alpha = null,
    int? Tokens = null,
    string? ContinuationToken = null,
    bool? IncludeInsights = null,
    bool? IncludeReasoning = null,
    string[]? Categories = null);

/// <summary>
/// Response with search results and pagination
/// </summary>
public record GetLibraryDocsResponse(
    string LibraryId,
    string LibraryName,
    string Query,
    SearchResult Result,
    bool HasMore,
    string IndexingStatus);
