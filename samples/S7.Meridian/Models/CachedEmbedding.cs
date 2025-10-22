namespace Koan.Samples.Meridian.Models;

/// <summary>
/// Represents a cached embedding for reuse across processing runs.
/// </summary>
public sealed class CachedEmbedding
{
    /// <summary>
    /// SHA-256 hash of the source text.
    /// </summary>
    public required string ContentHash { get; init; }

    /// <summary>
    /// Model identifier (e.g., "granite3.3:8b").
    /// </summary>
    public required string ModelId { get; init; }

    /// <summary>
    /// The embedding vector.
    /// </summary>
    public required float[] Embedding { get; init; }

    /// <summary>
    /// Dimensionality of the embedding.
    /// </summary>
    public required int Dimension { get; init; }

    /// <summary>
    /// Timestamp when this embedding was cached.
    /// </summary>
    public required DateTimeOffset CachedAt { get; init; }
}
