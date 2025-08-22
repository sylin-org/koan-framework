namespace Sora.AI.Contracts.Models;

public record AiChatChunk
{
    public string? DeltaText { get; init; }
    public int Index { get; init; }
    public string? Model { get; init; }
    public string? AdapterId { get; init; }
    public int? TokensOutInc { get; init; }
}