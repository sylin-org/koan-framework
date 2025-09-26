namespace S13.DocMind.Services;

public interface IEmbeddingGenerator
{
    Task<float[]?> GenerateAsync(string text, CancellationToken cancellationToken);
}
