namespace Sora.Messaging;

public sealed class MessagingOptions
{
    // Default bus code to resolve when none is specified. Prefer 'rabbit' for OOTB dev.
    public string? DefaultBus { get; set; } = "rabbit";
    // Default group used by providers when auto-subscribing without explicit Subscriptions
    public string DefaultGroup { get; set; } = "workers";
    // When true, include @v{Version} in type aliases derived from [Message]
    public bool IncludeVersionInAlias { get; set; } = false;
}