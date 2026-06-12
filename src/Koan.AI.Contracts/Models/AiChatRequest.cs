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

    /// <summary>
    /// AI-0035: Caller-supplied URL override. When set, the router bypasses source / member
    /// resolution and dispatches directly to this endpoint. The caller assumes ownership of
    /// the routing concerns (health, enable/disable, capability tracking) that source-backed
    /// requests inherit from the registry. Pair with <see cref="OverrideProvider"/> to select
    /// the adapter.
    /// </summary>
    public string? OverrideUrl { get; init; }

    /// <summary>
    /// AI-0035: Provider identifier used to select the adapter when <see cref="OverrideUrl"/>
    /// is set (e.g. <c>"ollama"</c>, <c>"lmstudio"</c>). Defaults to <c>"ollama"</c> when
    /// omitted. Must match an adapter registered with <c>IAiAdapterRegistry</c>.
    /// </summary>
    public string? OverrideProvider { get; init; }
}