using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using S13.DocMind.Models;

namespace S13.DocMind.Infrastructure;

public sealed class DocumentWorkItem
{
    private readonly object _lock = new();

    public DocumentWorkItem(
        Guid documentId,
        DocumentProcessingStage stage,
        DocumentProcessingStatus status = DocumentProcessingStatus.Queued,
        string? correlationId = null)
    {
        if (documentId == Guid.Empty)
        {
            throw new ArgumentException("Document id is required", nameof(documentId));
        }

        DocumentId = documentId;
        Stage = stage;
        Status = status;
        WorkId = Guid.NewGuid();
        CorrelationId = string.IsNullOrWhiteSpace(correlationId) ? Guid.NewGuid().ToString("N") : correlationId!;
        EnqueuedAt = DateTimeOffset.UtcNow;
        MaxAttempts = 3;
    }

    public Guid WorkId { get; }

    public Guid DocumentId { get; }

    public DocumentProcessingStage Stage { get; private set; }

    public DocumentProcessingStatus Status { get; private set; }

    public string CorrelationId { get; }

    public int Attempt { get; private set; }

    public int RetryCount { get; private set; }

    public int MaxAttempts { get; private set; }

    public DateTimeOffset EnqueuedAt { get; }

    public DateTimeOffset? LastDequeuedAt { get; private set; }

    public DateTimeOffset? LastAttemptCompletedAt { get; private set; }

    public Dictionary<string, string> Metadata { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool CanRetry => RetryCount < MaxAttempts;

    public void ConfigureMaxAttempts(int maxAttempts)
    {
        if (maxAttempts <= 0)
        {
            maxAttempts = 1;
        }

        lock (_lock)
        {
            MaxAttempts = maxAttempts;
        }
    }

    public void UpdateStage(DocumentProcessingStage stage, DocumentProcessingStatus status)
    {
        lock (_lock)
        {
            Stage = stage;
            Status = status;
        }
    }

    public void MarkDequeued()
    {
        lock (_lock)
        {
            Attempt++;
            LastDequeuedAt = DateTimeOffset.UtcNow;
        }
    }

    public void MarkCompleted(DocumentProcessingStatus status)
    {
        lock (_lock)
        {
            Status = status;
            LastAttemptCompletedAt = DateTimeOffset.UtcNow;
        }
    }

    public int RegisterRetry()
    {
        lock (_lock)
        {
            RetryCount++;
            return RetryCount;
        }
    }

    public override string ToString()
        => $"DocumentWorkItem {{ DocumentId = {DocumentId}, Stage = {Stage}, Attempt = {Attempt}, RetryCount = {RetryCount}, CorrelationId = {CorrelationId} }}";
}

public interface IDocumentPipelineQueue
{
    ValueTask EnqueueAsync(DocumentWorkItem workItem, CancellationToken ct = default);

    IAsyncEnumerable<DocumentWorkItem> DequeueAsync(CancellationToken ct = default);

    ValueTask<bool> ScheduleRetryAsync(DocumentWorkItem workItem, Exception reason, CancellationToken ct = default);

    ValueTask CompleteAsync(DocumentWorkItem workItem, CancellationToken ct = default);

    IReadOnlyCollection<DocumentQueueSnapshotItem> GetSnapshot();
}

public sealed class DocumentPipelineQueue : IDocumentPipelineQueue
{
    private readonly Channel<DocumentWorkItem> _channel;
    private readonly ILogger<DocumentPipelineQueue> _logger;
    private readonly IOptionsMonitor<DocMindOptions> _options;
    private readonly ConcurrentDictionary<Guid, DocumentWorkItem> _workItems = new();
    private readonly Random _random = new();

    public DocumentPipelineQueue(
        ILogger<DocumentPipelineQueue> logger,
        IOptionsMonitor<DocMindOptions> options)
    {
        _logger = logger;
        _options = options;
        var processing = options.CurrentValue.Processing;
        var capacity = Math.Max(1, processing.QueueCapacity);
        var channelOptions = new BoundedChannelOptions(capacity)
        {
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        };
        _channel = Channel.CreateBounded<DocumentWorkItem>(channelOptions);
    }

    public async ValueTask EnqueueAsync(DocumentWorkItem workItem, CancellationToken ct = default)
    {
        var processing = _options.CurrentValue.Processing;
        workItem.ConfigureMaxAttempts(processing.MaxRetryAttempts);
        workItem.UpdateStage(workItem.Stage, DocumentProcessingStatus.Queued);
        _workItems[workItem.WorkId] = workItem;
        _logger.LogDebug(
            "Queueing document {DocumentId} for stage {Stage} (correlation: {CorrelationId})",
            workItem.DocumentId,
            workItem.Stage,
            workItem.CorrelationId);
        await _channel.Writer.WriteAsync(workItem, ct);
    }

    public async IAsyncEnumerable<DocumentWorkItem> DequeueAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        while (await _channel.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
        {
            while (_channel.Reader.TryRead(out var item))
            {
                item.MarkDequeued();
                yield return item;
            }
        }
    }

    public async ValueTask<bool> ScheduleRetryAsync(DocumentWorkItem workItem, Exception reason, CancellationToken ct = default)
    {
        var attempt = workItem.RegisterRetry();
        var processing = _options.CurrentValue.Processing;
        if (attempt > processing.MaxRetryAttempts)
        {
            _logger.LogWarning(
                reason,
                "Dropping document {DocumentId} after exhausting {RetryCount} retries (correlation: {CorrelationId})",
                workItem.DocumentId,
                processing.MaxRetryAttempts,
                workItem.CorrelationId);
            _workItems.TryRemove(workItem.WorkId, out _);
            return false;
        }

        var delay = CalculateBackoffDelay(attempt - 1, processing);
        _logger.LogInformation(
            reason,
            "Scheduling retry {Retry}/{MaxRetry} for document {DocumentId} in {Delay} (correlation: {CorrelationId})",
            attempt,
            processing.MaxRetryAttempts,
            workItem.DocumentId,
            delay,
            workItem.CorrelationId);

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, ct).ConfigureAwait(false);
                await EnqueueAsync(workItem, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // shutting down; ignore
            }
        }, CancellationToken.None);

        return true;
    }

    public ValueTask CompleteAsync(DocumentWorkItem workItem, CancellationToken ct = default)
    {
        _workItems.TryRemove(workItem.WorkId, out _);
        return ValueTask.CompletedTask;
    }

    public IReadOnlyCollection<DocumentQueueSnapshotItem> GetSnapshot()
    {
        if (_workItems.IsEmpty)
        {
            return Array.Empty<DocumentQueueSnapshotItem>();
        }

        var snapshot = new List<DocumentQueueSnapshotItem>(_workItems.Count);
        foreach (var item in _workItems.Values)
        {
            snapshot.Add(DocumentQueueSnapshotItem.FromWorkItem(item));
        }

        return snapshot;
    }

    private TimeSpan CalculateBackoffDelay(int retryNumber, DocMindOptions.ProcessingOptions options)
    {
        var initialSeconds = Math.Max(1, options.RetryInitialDelaySeconds);
        var maxSeconds = Math.Max(initialSeconds, options.RetryMaxDelaySeconds);
        var multiplier = options.RetryBackoffMultiplier <= 1 ? 1d : options.RetryBackoffMultiplier;
        var baseDelayMs = initialSeconds * 1000d;
        if (retryNumber > 0)
        {
            baseDelayMs *= Math.Pow(multiplier, retryNumber);
        }

        var clampedMs = Math.Min(baseDelayMs, maxSeconds * 1000d);
        var delay = TimeSpan.FromMilliseconds(clampedMs);
        return ApplyJitter(delay, options);
    }

    private TimeSpan ApplyJitter(TimeSpan delay, DocMindOptions.ProcessingOptions options)
    {
        if (!options.RetryUseJitter || delay <= TimeSpan.Zero)
        {
            return delay;
        }

        lock (_random)
        {
            var jitterFactor = _random.NextDouble() * 0.3 + 0.85; // +/-15%
            var jittered = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * jitterFactor);
            var maxDelay = TimeSpan.FromSeconds(Math.Max(1, options.RetryMaxDelaySeconds));
            return jittered <= maxDelay ? jittered : maxDelay;
        }
    }
}

public sealed record DocumentQueueSnapshotItem(
    Guid WorkId,
    Guid DocumentId,
    DocumentProcessingStage Stage,
    DocumentProcessingStatus Status,
    int Attempt,
    int RetryCount,
    int MaxAttempts,
    DateTimeOffset EnqueuedAt,
    DateTimeOffset? LastDequeuedAt,
    DateTimeOffset? LastAttemptCompletedAt,
    string CorrelationId)
{
    public static DocumentQueueSnapshotItem FromWorkItem(DocumentWorkItem workItem)
        => new(
            workItem.WorkId,
            workItem.DocumentId,
            workItem.Stage,
            workItem.Status,
            workItem.Attempt,
            workItem.RetryCount,
            workItem.MaxAttempts,
            workItem.EnqueuedAt,
            workItem.LastDequeuedAt,
            workItem.LastAttemptCompletedAt,
            workItem.CorrelationId);
}
