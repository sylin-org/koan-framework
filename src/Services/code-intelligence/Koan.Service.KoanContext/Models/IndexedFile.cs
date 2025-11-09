using Koan.Data.Abstractions;
using Koan.Data.Core.Model;

namespace Koan.Context.Models;

/// <summary>
/// Tracks indexed files for SHA256-based differential scanning
/// </summary>
/// <remarks>
/// Simplified manifest for change detection:
/// - ContentHash (SHA256) detects actual content changes
/// - Only files with hash mismatches are re-indexed
/// - Removed: LastModified (unreliable OS metadata), ChunkCount (denormalized data)
/// Performance: 96-97% time savings on re-indexing (1% change rate scenario)
///
/// IMPORTANT: IndexedFile is a PARTITIONED entity (isolated by project).
/// - NO ProjectId field - partition context provides isolation (partition = proj-{projectId:N})
/// - ALL operations must occur within EntityContext.Partition(projectId)
/// - Contrast with SyncOperation: global entity with explicit ProjectId field for routing
///
/// Architectural Rationale:
/// - Per-project manifest table - partition provides natural isolation
/// - Each project has its own IndexedFile table (proj-{guid:N} partition)
/// - Queries within partition scope only see files for that project
/// - Clean separation: partitioned entities (IndexedFile, Chunk) vs global entities (SyncOperation, Job, Project)
/// </remarks>
public class IndexedFile : Entity<IndexedFile>
{
    /// <summary>Relative path from project root</summary>
    public string RelativePath { get; set; } = string.Empty;

    /// <summary>SHA256 hash of file contents</summary>
    public string ContentHash { get; set; } = string.Empty;

    /// <summary>When this file was last indexed</summary>
    public DateTime LastIndexedAt { get; set; } = DateTime.UtcNow;

    /// <summary>File size in bytes</summary>
    public long FileSize { get; set; }

    /// <summary>
    /// Creates indexed file record
    /// </summary>
    /// <remarks>
    /// Must be called within EntityContext.Partition(projectId) to ensure proper project isolation
    /// </remarks>
    public static IndexedFile Create(
        string relativePath,
        string contentHash,
        long fileSize)
    {
        return new IndexedFile
        {
            RelativePath = relativePath,
            ContentHash = contentHash,
            FileSize = fileSize,
            LastIndexedAt = DateTime.UtcNow
        };
    }

    /// <summary>Updates hash and metadata after re-indexing</summary>
    public void UpdateAfterIndexing(string contentHash, long fileSize)
    {
        ContentHash = contentHash;
        FileSize = fileSize;
        LastIndexedAt = DateTime.UtcNow;
    }
}
