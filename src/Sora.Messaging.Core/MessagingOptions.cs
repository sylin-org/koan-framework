namespace Sora.Messaging;

public sealed class MessagingOptions
{
    public string? DefaultBus { get; set; }
    // Default group used by providers when auto-subscribing without explicit Subscriptions
    public string DefaultGroup { get; set; } = "workers";
    // When true, include @v{Version} in type aliases derived from [Message]
    public bool IncludeVersionInAlias { get; set; } = false;
}