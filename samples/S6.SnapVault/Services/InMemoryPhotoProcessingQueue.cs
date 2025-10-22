using System.Collections.Concurrent;

namespace S6.SnapVault.Services;

/// <summary>
/// In-memory implementation of photo processing queue
/// Uses ConcurrentQueue for thread-safe operations and SemaphoreSlim for async signaling
/// </summary>
public class InMemoryPhotoProcessingQueue : IPhotoProcessingQueue
{
    private readonly ConcurrentQueue<QueuedPhotoUpload> _queue = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly ILogger<InMemoryPhotoProcessingQueue> _logger;

    public InMemoryPhotoProcessingQueue(ILogger<InMemoryPhotoProcessingQueue> logger)
    {
        _logger = logger;
    }

    public void Enqueue(QueuedPhotoUpload upload)
    {
        _queue.Enqueue(upload);
        _logger.LogDebug("Queued photo {FileName} for job {JobId} (queue size: {Count})",
            upload.FileName, upload.JobId, _queue.Count);

        // Signal that an item is available
        _signal.Release();
    }

    public bool TryDequeue(out QueuedPhotoUpload? upload)
    {
        var result = _queue.TryDequeue(out var item);
        upload = item;

        if (result)
        {
            _logger.LogDebug("Dequeued photo {FileName} for job {JobId} (remaining: {Count})",
                upload!.FileName, upload.JobId, _queue.Count);
        }

        return result;
    }

    public int Count => _queue.Count;

    public void SignalAvailable()
    {
        if (_queue.Count > 0)
        {
            _signal.Release();
        }
    }

    public async Task WaitForItemsAsync(CancellationToken ct)
    {
        await _signal.WaitAsync(ct);
    }
}
