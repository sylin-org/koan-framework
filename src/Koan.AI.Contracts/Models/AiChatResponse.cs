namespace Koan.AI.Contracts.Models;

public record AiChatResponse
{
    public string Text { get; init; } = "";
    public string? FinishReason { get; init; }
    public int? TokensIn { get; init; }
    public int? TokensOut { get; init; }
    public string? Model { get; init; }
    public string? AdapterId { get; init; }
}