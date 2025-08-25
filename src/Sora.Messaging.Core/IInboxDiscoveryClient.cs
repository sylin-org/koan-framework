namespace Sora.Messaging;

public interface IInboxDiscoveryClient
{
    // Attempts to discover an inbox endpoint for the default bus/group context.
    // Returns a base URL or null if none found within the timeout defined by DiscoveryOptions.
    Task<string?> DiscoverAsync(CancellationToken ct = default);
}