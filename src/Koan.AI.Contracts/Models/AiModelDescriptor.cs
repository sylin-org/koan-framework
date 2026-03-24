namespace Koan.AI.Contracts.Models;

public record AiModelDescriptor
{
    public string Name { get; init; } = "";
    public string? Family { get; init; }
    public int? ContextWindow { get; init; }
    public int? EmbeddingDim { get; init; }
    public string AdapterId { get; init; } = "";
    public string AdapterType { get; init; } = "";
}