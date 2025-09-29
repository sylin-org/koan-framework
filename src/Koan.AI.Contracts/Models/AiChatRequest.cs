namespace Koan.AI.Contracts.Models;

public record AiChatRequest
{
    public List<AiMessage> Messages { get; init; } = new();
    public string? Model { get; init; }
    public AiPromptOptions? Options { get; init; }
    public AiRouteHints? Route { get; init; }
    public AiConversationContext? Context { get; init; }
    public List<AiAugmentationInvocation> Augmentations { get; init; } = new();
}