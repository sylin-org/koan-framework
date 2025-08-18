namespace Sora.Messaging;

/// Inbox store for consumer-side de-duplication.
/// Tracks processed message keys so duplicate deliveries can be safely ignored.
public interface IInboxStore
{
    Task<bool> IsProcessedAsync(string key, CancellationToken ct = default);
    Task MarkProcessedAsync(string key, CancellationToken ct = default);
}
