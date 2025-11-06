using Koan.Context.Models;

namespace Koan.Context.Services;

/// <summary>
/// Service for retrieving and searching indexed documents
/// </summary>
public interface IRetrievalService
{
    /// <summary>
    /// Searches for documents matching the query
    /// </summary>
    /// <param name="projectId">Project to search within</param>
    /// <param name="query">Search query text</param>
    /// <param name="options">Search options (alpha, topK, etc.)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Search results with provenance</returns>
    Task<SearchResult> SearchAsync(
        string projectId,
        string query,
        SearchOptions? options = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Options for semantic search
/// </summary>
public record SearchOptions(
    float Alpha = 0.7f, // 0.0 = pure keyword (BM25), 1.0 = pure semantic (vector)
    int TopK = 10,
    int? OffsetStart = null,
    int? OffsetEnd = null);

/// <summary>
/// Search result with chunks and metadata
/// </summary>
public record SearchResult(
    IReadOnlyList<SearchResultChunk> Chunks,
    int TotalCount,
    TimeSpan Duration);

/// <summary>
/// Individual search result chunk with provenance
/// </summary>
public record SearchResultChunk(
    string Text,
    string FilePath,
    string? CommitSha,
    string? ChunkRange,
    string? Title,
    string? Language,
    float Score);
