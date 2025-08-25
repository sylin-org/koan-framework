namespace Sora.AI.Contracts.Models;

public record AiModelDescriptor
{
    public string Name { get; init; } = string.Empty;
    public string? Family { get; init; }
    public int? ContextWindow { get; init; }
    public int? EmbeddingDim { get; init; }
    public string AdapterId { get; init; } = string.Empty;
    public string AdapterType { get; init; } = string.Empty;
}