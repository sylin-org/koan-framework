namespace Sora.AI.Contracts.Models;

public record AiEmbeddingsResponse
{
    public List<float[]> Vectors { get; init; } = new();
    public string? Model { get; init; }
    public int? Dimension { get; init; }
}