using Koan.Data.Abstractions;
using Koan.Data.Core.Model;

namespace Koan.Context.Models;

/// <summary>
/// Represents an indexed file with content hash for differential scanning
/// </summary>
/// <remarks>
/// Stores file-level metadata to enable smart differential indexing:
/// - SHA256 hash detects actual content changes
/// - Timestamp+hash comparison determines if re-indexing is needed
/// - Supports three-tier change detection: new/changed/metadata-only
/// </remarks>
public class IndexedFile : Entity<IndexedFile>
{
    /// <summary>
    /// Project this file belongs to
    /// </summary>
    public string ProjectId { get; set; } = string.Empty;

    /// <summary>
    /// Relative path from project root
    /// </summary>
    public string RelativePath { get; set; } = string.Empty;

    /// <summary>
    /// SHA256 hash of file contents
    /// </summary>
    public string FileHash { get; set; } = string.Empty;

    /// <summary>
    /// Last modified timestamp from file system
    /// </summary>
    public DateTime LastModified { get; set; }

    /// <summary>
    /// File size in bytes
    /// </summary>
    public long SizeBytes { get; set; }

    /// <summary>
    /// When this file was last successfully indexed
    /// </summary>
    public DateTime LastIndexed { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Number of chunks created from this file
    /// </summary>
    public int ChunkCount { get; set; }

    /// <summary>
    /// Creates or updates an indexed file record
    /// </summary>
    public static IndexedFile Create(
        string projectId,
        string relativePath,
        string fileHash,
        DateTime lastModified,
        long sizeBytes,
        int chunkCount = 0)
    {
        return new IndexedFile
        {
            ProjectId = projectId,
            RelativePath = relativePath,
            FileHash = fileHash,
            LastModified = lastModified,
            SizeBytes = sizeBytes,
            ChunkCount = chunkCount,
            LastIndexed = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Updates the hash and metadata after re-indexing
    /// </summary>
    public void UpdateAfterIndexing(string fileHash, DateTime lastModified, long sizeBytes, int chunkCount)
    {
        FileHash = fileHash;
        LastModified = lastModified;
        SizeBytes = sizeBytes;
        ChunkCount = chunkCount;
        LastIndexed = DateTime.UtcNow;
    }
}
