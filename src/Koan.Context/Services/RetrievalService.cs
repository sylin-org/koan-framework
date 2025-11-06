using System.Diagnostics;
using Koan.Context.Models;
using Koan.Data.Core;
using Koan.Data.Vector;
using Koan.Data.Vector.Abstractions;
using Microsoft.Extensions.Logging;

namespace Koan.Context.Services;

/// <summary>
/// Service for semantic and hybrid search over indexed documents
/// </summary>
public class RetrievalService : IRetrievalService
{
    private readonly IEmbeddingService _embedding;
    private readonly ILogger<RetrievalService> _logger;

    public RetrievalService(
        IEmbeddingService embedding,
        ILogger<RetrievalService> logger)
    {
        _embedding = embedding ?? throw new ArgumentNullException(nameof(embedding));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<SearchResult> SearchAsync(
        string projectId,
        string query,
        SearchOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new SearchResult(Array.Empty<SearchResultChunk>(), 0, TimeSpan.Zero);
        }

        var stopwatch = Stopwatch.StartNew();
        options ??= new SearchOptions();

        _logger.LogInformation(
            "Searching project {ProjectId} for query: {Query} (alpha={Alpha}, topK={TopK})",
            projectId,
            query,
            options.Alpha,
            options.TopK);

        try
        {
            // Set partition context for this project
            // Use "proj-" prefix to ensure valid partition name (must start with letter)
            // Parse projectId as GUID and format without hyphens
            var partitionId = $"proj-{Guid.Parse(projectId):N}";
            using (EntityContext.Partition(partitionId))
            {
                // Generate query embedding
                var queryEmbedding = await _embedding.EmbedAsync(query, cancellationToken);

                if (queryEmbedding.Length == 0)
                {
                    _logger.LogWarning("Empty embedding generated for query: {Query}", query);
                    return new SearchResult(Array.Empty<SearchResultChunk>(), 0, stopwatch.Elapsed);
                }

                // Perform hybrid vector search
                var searchResult = await Vector<DocumentChunk>.Search(
                    vector: queryEmbedding,
                    text: query,
                    alpha: options.Alpha,
                    topK: options.TopK,
                    ct: cancellationToken);

                // Load full DocumentChunk entities for each match
                var chunks = new List<SearchResultChunk>();
                foreach (var match in searchResult.Matches)
                {
                    var chunk = await DocumentChunk.Get(match.Id, cancellationToken);
                    if (chunk != null)
                    {
                        chunks.Add(new SearchResultChunk(
                            Text: chunk.SearchText,
                            FilePath: chunk.FilePath,
                            CommitSha: chunk.CommitSha,
                            ChunkRange: chunk.ChunkRange,
                            Title: chunk.Title,
                            Language: chunk.Language,
                            Score: (float)match.Score
                        ));
                    }
                }

                stopwatch.Stop();

                _logger.LogInformation(
                    "Search completed: {ResultCount} results in {Duration}ms",
                    chunks.Count,
                    stopwatch.ElapsedMilliseconds);

                return new SearchResult(chunks, chunks.Count, stopwatch.Elapsed);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Search failed for project {ProjectId}", projectId);
            throw;
        }
    }
}
