namespace Koan.AI.Contracts.Models;

public record AiChatRequest
{
    public List<AiMessage> Messages { get; init; } = new();
    public string? Model { get; init; }
    public AiPromptOptions? Options { get; init; }
    public AiRouteHints? Route { get; init; }
    public AiConversationContext? Context { get; init; }
    public List<AiAugmentationInvocation> Augmentations { get; init; } = new();

    /// <summary>
    /// ADR-0015: Internal property set by router to inject member URL into adapter.
    /// Adapters use this to route to specific endpoints in singleton pattern.
    /// </summary>
    public string? InternalConnectionString { get; set; }
}