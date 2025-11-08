using Koan.Context.Models;
using Koan.Context.Services;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Koan.Context.Controllers;

/// <summary>
/// MCP tool for Koan Context semantic code search
/// Provides a single, simplified API for retrieving code references
/// </summary>
[ApiController]
[Route("api/mcp")]
public class McpToolsController : ControllerBase
{
    private readonly Search _retrieval;
    private readonly Indexer _indexing;
    private readonly ProjectResolver _projectResolver;
    private readonly ILogger<McpToolsController> _logger;

    public McpToolsController(
        Search retrieval,
        Indexer indexing,
        ProjectResolver projectResolver,
        ILogger<McpToolsController> logger)
    {
        _retrieval = retrieval;
        _indexing = indexing;
        _projectResolver = projectResolver;
        _logger = logger;
    }

    /// <summary>
    /// MCP Tool: koan-context (get-references)
    /// Performs semantic code search within a project directory
    /// </summary>
    /// <param name="request">Search request with working directory and query</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Code references matching the semantic query</returns>
    [HttpPost("get-references")]
    public async Task<IActionResult> GetReferences(
        [FromBody] GetReferencesRequest request,
        CancellationToken cancellationToken)
    {
        // Validate required parameters
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return BadRequest(new
            {
                error = "missing_required_parameter",
                message = "The 'query' parameter is required. Please provide a search query describing what code references you're looking for.",
                requiredParameters = new[] { "query", "workingDirectory" },
                example = new
                {
                    query = "authentication middleware",
                    workingDirectory = "/path/to/your/project"
                }
            });
        }

        if (string.IsNullOrWhiteSpace(request.WorkingDirectory))
        {
            return BadRequest(new
            {
                error = "missing_required_parameter",
                message = "The 'workingDirectory' parameter is required. Please provide the path to the project directory you want to search.",
                requiredParameters = new[] { "query", "workingDirectory" },
                example = new
                {
                    query = "authentication middleware",
                    workingDirectory = "/path/to/your/project"
                }
            });
        }

        try
        {
            _logger.LogInformation(
                "Searching project at {WorkingDirectory} for: {Query}",
                request.WorkingDirectory,
                request.Query);

            // Resolve project from working directory (auto-creates if needed)
            var project = await _projectResolver.ResolveProjectAsync(
                libraryId: null,
                workingDirectory: request.WorkingDirectory,
                httpContext: HttpContext,
                autoCreate: true,
                cancellationToken);

            if (project == null)
            {
                return BadRequest(new
                {
                    error = "invalid_working_directory",
                    message = $"Could not resolve project from working directory: {request.WorkingDirectory}",
                    workingDirectory = request.WorkingDirectory
                });
            }

            // Check indexing status and handle appropriately
            switch (project.Status)
            {
                case IndexingStatus.NotIndexed:
                    // Start indexing in background
                    project.Status = IndexingStatus.Indexing;
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
                        projectName = project.Name,
                        projectPath = project.RootPath,
                        estimatedDuration = "2-5 minutes",
                        retryAfter = 120
                    });

                case IndexingStatus.Indexing:
                    // Project is indexing but still queryable - proceed with search (partial results)
                    _logger.LogDebug("Project {Name} is indexing, but proceeding with query (partial results)", project.Name);
                    break;

                case IndexingStatus.Failed:
                    return StatusCode(500, new
                    {
                        error = "indexing_failed",
                        message = $"Project '{project.Name}' indexing failed: {project.LastError}",
                        projectName = project.Name,
                        projectPath = project.RootPath,
                        indexingError = project.LastError
                    });

                case IndexingStatus.Ready:
                    // Proceed with search
                    break;
            }

            // Perform semantic search
            var searchOptions = new SearchOptions(
                MaxTokens: request.TokenCounter ?? 5000,
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
                "Found {ResultCount} references in {ProjectName}",
                result.Chunks.Count,
                project.Name);

            var response = new GetReferencesResponse(
                ProjectName: project.Name,
                ProjectPath: project.RootPath,
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
                "Failed to get references for query: {Query} in {WorkingDirectory}",
                request.Query,
                request.WorkingDirectory);

            return StatusCode(500, new
            {
                error = "search_failed",
                message = ex.Message,
                query = request.Query,
                workingDirectory = request.WorkingDirectory
            });
        }
    }
}

/// <summary>
/// Request for semantic code search
/// </summary>
public record GetReferencesRequest(
    string Query,
    string WorkingDirectory,
    float? Alpha = null,
    int? TokenCounter = null,
    string? ContinuationToken = null,
    bool? IncludeInsights = null,
    bool? IncludeReasoning = null,
    string[]? Categories = null);

/// <summary>
/// Response with code references and metadata
/// </summary>
public record GetReferencesResponse(
    string ProjectName,
    string ProjectPath,
    string Query,
    SearchResult Result,
    bool HasMore,
    string IndexingStatus);
