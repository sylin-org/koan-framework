using Koan.Data.Abstractions;
using Koan.Data.Core.Model;
using Koan.Data.Vector.Abstractions.Schema;

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
/// IMPORTANT: Chunks are isolated by partition context (raw project ID), not by ProjectId field.
/// All chunk operations must occur within EntityContext.Partition() to ensure proper isolation.
/// </remarks>
[VectorSchema(typeof(ChunkVectorMetadata), EntityName = "KoanChunkVector")]
public class Chunk : Entity<Chunk>
{
    /// <summary>
    /// Foreign key to IndexedFile (proper relational integrity)
    /// </summary>
    /// <remarks>
    /// Links this chunk to its source file in the IndexedFile manifest.
    /// Provides GUID-based relationship instead of error-prone string matching.
    /// IndexedFile lives in root table, Chunk lives in partition table - cross-boundary FK is valid.
    /// </remarks>
    public string IndexedFileId { get; set; } = string.Empty;

    /// <summary>
    /// Relative file path within the project (denormalized for performance)
    /// </summary>
    /// <remarks>
    /// Denormalized from IndexedFile.RelativePath for fast search result display.
    /// Single source of truth is IndexedFile.RelativePath (via IndexedFileId FK).
    /// </remarks>
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
    /// Path segments for context (e.g., ["docs", "decisions"])
    /// </summary>
    public string[]? PathSegments { get; set; }

    /// <summary>
    /// Structured tag metadata (primary, secondary, file-level inheritance, audit trail).
    /// </summary>
    public TagEnvelope Tags { get; set; } = TagEnvelope.Empty;

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
        string indexedFileId,
        string filePath,
        string searchText,
        int tokenCount,
        string? commitSha = null,
        string? title = null,
        string? language = null)
    {
        if (string.IsNullOrWhiteSpace(indexedFileId))
            throw new ArgumentException("IndexedFileId cannot be empty", nameof(indexedFileId));

        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("FilePath cannot be empty", nameof(filePath));

        if (string.IsNullOrWhiteSpace(searchText))
            throw new ArgumentException("SearchText cannot be empty", nameof(searchText));

        return new Chunk
        {
            IndexedFileId = indexedFileId,
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

public static class ChunkTagExtensions
{
    public static TagEnvelope GetTagEnvelope(this Chunk chunk)
        => chunk.Tags;

    public static IReadOnlyList<string> GetPrimaryTags(this Chunk chunk)
        => chunk.Tags.Primary;

    public static IReadOnlyList<string> GetSecondaryTags(this Chunk chunk)
        => chunk.Tags.Secondary;

    public static IReadOnlyList<TagAuditEntry> GetAuditTrail(this Chunk chunk)
        => chunk.Tags.Audit;

    public static void SetTagEnvelope(this Chunk chunk, TagEnvelope envelope)
    {
        chunk.Tags = envelope.Normalize();
    }

    public static void SetPrimaryTags(this Chunk chunk, IEnumerable<string>? tags)
    {
        chunk.SetTagEnvelope(chunk.Tags.WithPrimary(tags));
    }

    public static void SetSecondaryTags(this Chunk chunk, IEnumerable<string>? tags)
    {
        chunk.SetTagEnvelope(chunk.Tags.WithSecondary(tags));
    }

    public static void SetAuditTrail(this Chunk chunk, IEnumerable<TagAuditEntry>? auditEntries)
    {
        chunk.SetTagEnvelope(chunk.Tags.WithAudit(auditEntries));
    }
}
