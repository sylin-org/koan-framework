namespace S5.Recs.Services;

/// <summary>
/// Represents a cached embedding with metadata for validation.
/// </summary>
public sealed class CachedEmbedding
{
    public required string ContentHash { get; init; }
    public required string ModelId { get; init; }
    public required float[] Embedding { get; init; }
    public required int Dimension { get; init; }
    public required DateTimeOffset CachedAt { get; init; }
}
