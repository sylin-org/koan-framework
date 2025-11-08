using Koan.Data.Abstractions;
using Koan.Data.Core.Model;

namespace Koan.Context.Models;

/// <summary>
/// Represents a chunk of documentation or code indexed in the vector store
/// </summary>
/// <remarks>
/// Each chunk is a semantically meaningful portion of a document (800-1000 tokens)
/// with metadata for provenance tracking and retrieval.
///
/// Storage Model:
/// - Relational (SQLite): Metadata via chunk.Save()
/// - Vector: Embedding via Vector&lt;Chunk&gt;.Save(chunk, embedding)
///
/// IMPORTANT: Chunks are isolated by partition context (proj-{guid:N}), not by ProjectId field.
/// All chunk operations must occur within EntityContext.Partition() to ensure proper isolation.
/// </remarks>
public class Chunk : Entity<Chunk>
{
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
    /// Byte offset where this chunk begins in the source file
    /// </summary>
    public long StartByteOffset { get; set; }

    /// <summary>
    /// Byte offset where this chunk ends in the source file
    /// </summary>
    public long EndByteOffset { get; set; }

    /// <summary>
    /// Starting line number for the chunk (1-based)
    /// </summary>
    public int StartLine { get; set; }

    /// <summary>
    /// Ending line number for the chunk (1-based, inclusive)
    /// </summary>
    public int EndLine { get; set; }

    /// <summary>
    /// Optional direct URL for the source file (e.g., GitHub blob link)
    /// </summary>
    public string? SourceUrl { get; set; }

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
    /// Creates a new Chunk with required fields
    /// </summary>
    /// <remarks>
    /// Must be called within EntityContext.Partition() to ensure proper project isolation
    /// </remarks>
    public static Chunk Create(
        string filePath,
        string searchText,
        int tokenCount,
        string? commitSha = null,
        string? title = null,
        string? language = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("FilePath cannot be empty", nameof(filePath));

        if (string.IsNullOrWhiteSpace(searchText))
            throw new ArgumentException("SearchText cannot be empty", nameof(searchText));

        return new Chunk
        {
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
