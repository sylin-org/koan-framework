using Koan.Data.Core.Model;

namespace S5.Recs.Models;

/// <summary>
/// Entity-based embedding cache for intelligent reuse via SHA256 content signatures.
/// Replaces file-based embedding cache for horizontal scalability and queryability.
/// Part of ARCH-0069: Partition-Based Import Pipeline Architecture.
/// </summary>
public class EmbeddingCacheEntry : Entity<EmbeddingCacheEntry, string>
{
    /// <summary>
    /// Composite ID: {contentSignature}:{modelId}:{entityType}
    /// Example: "a1b2c3d4...xyz:nomic-embed-text:Media"
    /// </summary>
    public override string Id { get; set; } = "";

    /// <summary>
    /// SHA256 hash of embedding text content (titles + synopsis + tags + genres)
    /// </summary>
    public required string ContentSignature { get; set; }

    /// <summary>
    /// Model ID that generated this embedding (e.g., "nomic-embed-text")
    /// </summary>
    public required string ModelId { get; set; }

    /// <summary>
    /// Entity type this embedding belongs to (e.g., "Media")
    /// </summary>
    public required string EntityType { get; set; }

    /// <summary>
    /// The embedding vector
    /// </summary>
    public required float[] Embedding { get; set; }

    /// <summary>
    /// Dimension of the embedding vector
    /// </summary>
    public int Dimension { get; set; }

    /// <summary>
    /// When this embedding was first cached
    /// </summary>
    public DateTimeOffset CachedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Last time this embedding was accessed (for LRU cleanup)
    /// </summary>
    public DateTimeOffset LastAccessedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Number of times this embedding has been accessed (cache hit count)
    /// </summary>
    public int AccessCount { get; set; } = 0;

    /// <summary>
    /// Generates composite cache ID from components
    /// </summary>
    public static string MakeCacheId(string contentSignature, string modelId, string entityType)
    {
        return $"{contentSignature}:{modelId}:{entityType}";
    }

    /// <summary>
    /// Updates access tracking metadata (call when cache hit occurs)
    /// </summary>
    public void RecordAccess()
    {
        LastAccessedAt = DateTimeOffset.UtcNow;
        AccessCount++;
    }
}
