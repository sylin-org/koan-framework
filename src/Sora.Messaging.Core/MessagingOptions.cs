namespace Sora.Messaging;

public sealed class MessagingOptions
{
    // Default bus code to resolve when none is specified. Prefer 'rabbit' for OOTB dev.
    public string? DefaultBus { get; set; } = "rabbit";
    // Default group used by providers when auto-subscribing without explicit Subscriptions
    public string DefaultGroup { get; set; } = "workers";
    // When true, include @v{Version} in type aliases derived from [Message]
    public bool IncludeVersionInAlias { get; set; } = false;

    // Enable DLQ creation for planner auto-provisioned queues (if provider supports it)
    public bool EnableDlq { get; set; } = true;
    // Enable retry bucket planning (provider-specific realization) when supported
    public bool EnableRetry { get; set; } = true;
    // Default retry policy applied when provider doesn't supply one explicitly
    public RetryOptions DefaultRetry { get; set; } = new();

    // Persist last applied plan hash to disk to allow fast no-op startup when unchanged
    public bool PersistPlanHash { get; set; } = true;
    // Directory (relative or absolute) for persisted plan hash files; defaults to AppContext.BaseDirectory if null/empty
    public string? PlanHashDirectory { get; set; } = null;
    // Optional: enable handler discovery (future enhancement). Currently placeholder for potential scan toggle.
    public bool EnableHandlerDiscovery { get; set; } = false;
}