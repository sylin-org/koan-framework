using Koan.Data.Abstractions;
using Koan.Data.Core.Model;

namespace Koan.Context.Models;

/// <summary>
/// Represents a chunk of documentation or code indexed in the vector store
/// </summary>
/// <remarks>
/// Each chunk is a semantically meaningful portion of a document (800-1000 tokens)
/// with metadata for provenance tracking and retrieval.
/// This entity is stored in both relational storage (for metadata queries)
/// and vector storage (for semantic search).
/// Uses string IDs for compatibility with Vector&lt;T&gt; partition-aware storage.
/// </remarks>
public class DocumentChunk : Entity<DocumentChunk>
{
    /// <summary>
    /// Project/partition ID this chunk belongs to
    /// </summary>
    public string ProjectId { get; set; } = string.Empty;

    /// <summary>
    /// Relative file path within the project
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Full text content of this chunk (indexed for BM25 hybrid search)
    /// </summary>
    public string SearchText { get; set; } = string.Empty;

    /// <summary>
    /// Git commit SHA for provenance (if available)
    /// </summary>
    public string? CommitSha { get; set; }

    /// <summary>
    /// Byte offset range in source file (format: "start:end")
    /// </summary>
    public string? ChunkRange { get; set; }

    /// <summary>
    /// Document title derived from heading hierarchy
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Programming language (for code blocks) or "markdown" for docs
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// When this chunk was created/indexed
    /// </summary>
    public DateTime IndexedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Token count of this chunk
    /// </summary>
    public int TokenCount { get; set; }

    /// <summary>
    /// Path-derived category (decision, guide, test, source, etc.)
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// Path segments for context (e.g., ["docs", "decisions"])
    /// </summary>
    public string[]? PathSegments { get; set; }

    /// <summary>
    /// Last modification time of the source file (for incremental updates)
    /// </summary>
    public DateTime FileLastModified { get; set; }

    /// <summary>
    /// SHA256 hash of file content (for change detection)
    /// </summary>
    public string? FileHash { get; set; }

    /// <summary>
    /// Creates a new DocumentChunk with required fields
    /// </summary>
    public static DocumentChunk Create(
        string projectId,
        string filePath,
        string searchText,
        int tokenCount,
        string? commitSha = null,
        string? title = null,
        string? language = null)
    {
        if (string.IsNullOrWhiteSpace(projectId))
            throw new ArgumentException("ProjectId cannot be empty", nameof(projectId));

        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("FilePath cannot be empty", nameof(filePath));

        if (string.IsNullOrWhiteSpace(searchText))
            throw new ArgumentException("SearchText cannot be empty", nameof(searchText));

        return new DocumentChunk
        {
            ProjectId = projectId,
            FilePath = filePath,
            SearchText = searchText,
            TokenCount = tokenCount,
            CommitSha = commitSha,
            Title = title,
            Language = language,
            IndexedAt = DateTime.UtcNow
        };
    }
}
