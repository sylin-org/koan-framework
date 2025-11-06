using Koan.Context.Models;

namespace Koan.Context.Services;

/// <summary>
/// Service for retrieving and searching indexed documents
/// </summary>
public interface IRetrievalService
{
    /// <summary>
    /// Searches for documents matching the query with AI-optimized response format
    /// </summary>
    /// <param name="projectId">Project to search within</param>
    /// <param name="query">Search query text</param>
    /// <param name="options">Search options (tokens, alpha, pagination, etc.)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>AI-optimized search results with provenance and insights</returns>
    Task<SearchResult> SearchAsync(
        string projectId,
        string query,
        SearchOptions? options = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Options for semantic search with AI optimization
/// </summary>
public record SearchOptions(
    int MaxTokens = 5000,           // Token budget (clamped 1000-10000)
    float Alpha = 0.7f,              // Hybrid search: 0.0 = keyword (BM25), 1.0 = semantic (vector)
    string? ContinuationToken = null, // Pagination token
    bool IncludeInsights = true,     // Include aggregated insights
    bool IncludeReasoning = true     // Include retrieval reasoning traces
);

/// <summary>
/// Enhanced search result with AI-optimized metadata
/// </summary>
public record SearchResult(
    IReadOnlyList<SearchResultChunk> Chunks,
    SearchMetadata Metadata,
    SearchSources Sources,
    SearchInsights? Insights,
    string? ContinuationToken,
    IReadOnlyList<string> Warnings
);

/// <summary>
/// Individual search result chunk with provenance and reasoning
/// </summary>
public record SearchResultChunk(
    string Id,                    // Unique identifier: "doc-1-chunk-0"
    string Text,                  // Markdown/code content
    float Score,                  // Hybrid relevance score (0.0 - 1.0)
    ChunkProvenance Provenance,
    RetrievalReasoning? Reasoning
);

/// <summary>
/// Detailed provenance for traceability and citation
/// </summary>
public record ChunkProvenance(
    int SourceIndex,        // Index into SearchSources.Files
    long StartByteOffset,   // Precise byte position
    long EndByteOffset,
    int StartLine,          // Backward compatibility
    int EndLine,
    string? Language        // "typescript", "markdown", etc.
);

/// <summary>
/// Search execution metadata
/// </summary>
public record SearchMetadata(
    int TokensRequested,    // Budget requested
    int TokensReturned,     // Actual tokens consumed
    int Page,               // Current page number
    string Model,           // "all-minilm"
    string VectorProvider,  // "weaviate"
    DateTime Timestamp,
    TimeSpan Duration
);

/// <summary>
/// Deduplicated source files
/// </summary>
public record SearchSources(
    int TotalFiles,
    IReadOnlyList<SourceFile> Files
);

public record SourceFile(
    string FilePath,
    string? Title,
    string? Url,            // Direct GitHub/GitLab URL
    string CommitSha
);

/// <summary>
/// Lean reasoning trace for AI explainability
/// </summary>
public record RetrievalReasoning(
    float SemanticScore,    // Vector similarity (0.0 - 1.0)
    float KeywordScore,     // BM25 text match (0.0 - 1.0)
    string Strategy         // "vector" | "keyword" | "hybrid"
);

/// <summary>
/// Aggregated insights across all chunks
/// </summary>
public record SearchInsights(
    IReadOnlyDictionary<string, int> Topics,  // Topic clusters: {"routing": 5, "middleware": 3}
    string CompletenessLevel,                 // "comprehensive" | "partial" | "insufficient"
    IReadOnlyList<string>? MissingTopics      // Suggested follow-up queries
);
