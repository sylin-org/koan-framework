namespace Sora.AI.Contracts.Models;

public record AiChatRequest
{
    public List<AiMessage> Messages { get; init; } = new();
    public string? Model { get; init; }
    public AiPromptOptions? Options { get; init; }
    public AiRouteHints? Route { get; init; }
}