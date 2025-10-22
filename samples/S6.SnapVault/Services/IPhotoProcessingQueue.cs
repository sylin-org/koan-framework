namespace S6.SnapVault.Services;

/// <summary>
/// Queue for background photo processing
/// </summary>
public interface IPhotoProcessingQueue
{
    /// <summary>
    /// Enqueue a photo for background processing
    /// </summary>
    void Enqueue(QueuedPhotoUpload upload);

    /// <summary>
    /// Try to dequeue a photo for processing
    /// </summary>
    bool TryDequeue(out QueuedPhotoUpload? upload);

    /// <summary>
    /// Get the current queue count
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Signal that new items are available for processing
    /// </summary>
    void SignalAvailable();

    /// <summary>
    /// Wait asynchronously for items to become available
    /// </summary>
    Task WaitForItemsAsync(CancellationToken ct);
}
