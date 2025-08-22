namespace Sora.Messaging;

public sealed class DiscoveryOptions
{
    // When null, environment defaults apply (On in non-Production, Off in Production unless Magic flag is set)
    public bool? Enabled { get; set; }
    public int TimeoutSeconds { get; set; } = 3;
    public int CacheMinutes { get; set; } = 5;
    // Optional small wait to collect multiple announces and choose the best endpoint
    public int SelectionWaitMs { get; set; } = 150;
}