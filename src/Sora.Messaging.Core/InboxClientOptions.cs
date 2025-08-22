namespace Sora.Messaging;

public sealed class InboxClientOptions
{
    // Explicit external inbox endpoint; when set, discovery is skipped.
    public string? Endpoint { get; set; }
    // If true and no inbox is resolved, client may fail-closed (future use)
    public bool Required { get; set; } = false;
}