namespace S13.DocMind.Services;

public interface IEmbeddingGenerator
{
    Task<EmbeddingGenerationResult> GenerateAsync(string text, CancellationToken cancellationToken);
}

public sealed record EmbeddingGenerationResult(float[]? Embedding, TimeSpan Duration, string? Model)
{
    public static readonly EmbeddingGenerationResult Empty = new(null, TimeSpan.Zero, null);

    public bool HasEmbedding => Embedding is { Length: > 0 };
}
