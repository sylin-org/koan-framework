namespace Sora.AI.Contracts.Models;

public record AiEmbeddingsRequest
{
    public List<string> Input { get; init; } = new();
    public string? Model { get; init; }
}