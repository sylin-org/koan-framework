using System;
using System.Collections.Generic;
using System.Threading;
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
    private readonly ISearchService _retrieval;
    private readonly IndexProjectAsync _indexProject;
    private readonly ProjectResolver _projectResolver;
    private readonly ILogger<McpToolsController> _logger;

    public McpToolsController(
    ISearchService retrieval,
    IndexProjectAsync indexProject,
        ProjectResolver projectResolver,
        ILogger<McpToolsController> logger)
    {
        _retrieval = retrieval;
        _indexProject = indexProject ?? throw new ArgumentNullException(nameof(indexProject));
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
                _logger.LogWarning(
                    "Working directory {WorkingDirectory} did not resolve to a project. Falling back to multi-project search.",
                    request.WorkingDirectory);

                return await SearchAllProjectsAsync(request, cancellationToken);
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
                            await _indexProject(project.Id, force: false, cancellationToken: CancellationToken.None);
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

            var context = SearchRequestContext.Create(
                query: request.Query,
                projectIds: new[] { project.Id },
                pathContext: null,
                tagsAny: request.TagsAny,
                tagsAll: request.TagsAll,
                tagsExclude: request.TagsExclude,
                tagBoosts: request.TagBoosts,
                personaId: request.Persona,
                channel: SearchChannel.Mcp,
                continuationToken: request.ContinuationToken,
                maxTokens: request.MaxTokens,
                includeInsights: false,
                includeReasoning: request.IncludeReasoning ?? false,
                languages: request.Languages);

            var result = await _retrieval.SearchAsync(
                project.Id,
                context,
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

    private async Task<IActionResult> SearchAllProjectsAsync(
        GetReferencesRequest request,
        CancellationToken cancellationToken)
    {
    var projects = await Project.Query(_ => true, cancellationToken);
        if (projects.Count == 0)
        {
            return NotFound(new
            {
                error = "no_projects",
                message = "No indexed projects are available for fallback search.",
                workingDirectory = request.WorkingDirectory
            });
        }

        const int estimatedTokensPerChunk = 350;
        var tokenBudget = Math.Clamp(request.MaxTokens ?? 4000, 1000, 20000);
        var accumulatedChunks = new List<SearchResultChunk>();
        var accumulatedSources = new List<SourceFile>();
        var warnings = new List<string>
        {
            "Working directory did not map to a project; returning multi-project fallback results."
        };

        var tokensConsumed = 0;
        var hasMore = false;

        foreach (var project in projects)
        {
            if (hasMore)
            {
                break;
            }

            var context = SearchRequestContext.Create(
                query: request.Query,
                projectIds: new[] { project.Id },
                pathContext: null,
                tagsAny: request.TagsAny,
                tagsAll: request.TagsAll,
                tagsExclude: request.TagsExclude,
                tagBoosts: request.TagBoosts,
                personaId: request.Persona,
                channel: SearchChannel.Mcp,
                continuationToken: null,
                maxTokens: tokenBudget,
                includeInsights: false,
                includeReasoning: false,
                languages: request.Languages);

            var result = await _retrieval.SearchAsync(project.Id, context, cancellationToken);

            if (result.Chunks.Count == 0)
            {
                continue;
            }

            var sourceBaseIndex = accumulatedSources.Count;
            if (result.Sources?.Files != null)
            {
                accumulatedSources.AddRange(result.Sources.Files);
            }

            foreach (var chunk in result.Chunks)
            {
                if (tokensConsumed + estimatedTokensPerChunk > tokenBudget && accumulatedChunks.Count > 0)
                {
                    hasMore = true;
                    break;
                }

                var adjustedProvenance = chunk.Provenance with
                {
                    SourceIndex = sourceBaseIndex + chunk.Provenance.SourceIndex
                };

                accumulatedChunks.Add(chunk with { Provenance = adjustedProvenance });
                tokensConsumed += estimatedTokensPerChunk;
            }

            if (tokensConsumed >= tokenBudget)
            {
                hasMore = true;
            }
        }

        var metadata = new SearchMetadata(
            TokensRequested: tokenBudget,
            TokensReturned: Math.Min(tokensConsumed, tokenBudget),
            Page: 1,
            Model: nameof(Search),
            VectorProvider: "default",
            Timestamp: DateTime.UtcNow,
            Duration: TimeSpan.Zero);

        var sources = new SearchSources(
            TotalFiles: accumulatedSources.Count,
            Files: accumulatedSources);

        var fallbackResult = new SearchResult(
            Chunks: accumulatedChunks,
            Metadata: metadata,
            Sources: sources,
            Insights: null,
            ContinuationToken: null,
            Warnings: warnings);

        var response = new GetReferencesResponse(
            ProjectName: "all-projects",
            ProjectPath: string.Empty,
            Query: request.Query,
            Result: fallbackResult,
            HasMore: hasMore,
            IndexingStatus: "Aggregate");

        return Ok(response);
    }
}

/// <summary>
/// Request for semantic code search
/// </summary>
public record GetReferencesRequest(
    string Query,
    string WorkingDirectory,
    string? Persona = null,
    int? MaxTokens = null,
    string? ContinuationToken = null,
    bool? IncludeInsights = null,
    bool? IncludeReasoning = null,
    string[]? TagsAny = null,
    string[]? TagsAll = null,
    string[]? TagsExclude = null,
    Dictionary<string, float>? TagBoosts = null,
    string[]? Languages = null);

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
