using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace S13.DocMind.Infrastructure;

public record ProcessingProfile(string Name, string? TextModel = null, string? VisionModel = null, string? EmbeddingModel = null)
{
    public static ProcessingProfile Default(string? embeddingModel = null) => new("default", embeddingModel: embeddingModel);
}

public sealed class DocumentWorkItem
{
    private readonly object _lock = new();

    public DocumentWorkItem(
        string fileId,
        string? documentTypeId,
        ProcessingProfile profile,
        string? correlationId = null,
        string? traceId = null,
        string? spanId = null)
    {
        FileId = fileId;
        DocumentTypeId = documentTypeId;
        Profile = profile;
        CorrelationId = correlationId ?? Guid.NewGuid().ToString("N");
        TraceId = traceId ?? Guid.NewGuid().ToString("N");
        SpanId = spanId ?? Guid.NewGuid().ToString("N");
        EnqueuedAt = DateTimeOffset.UtcNow;
        MaxAttempts = 3;
    }

    public string FileId { get; }
    public string? DocumentTypeId { get; }
    public ProcessingProfile Profile { get; }

    public string CorrelationId { get; }
    public string TraceId { get; }
    public string SpanId { get; }

    public int Attempt { get; private set; }
    public int RetryCount { get; private set; }
    public int ConsecutiveFailures { get; private set; }
    public int MaxAttempts { get; internal set; }

    public DateTimeOffset EnqueuedAt { get; }
    public DateTimeOffset? LastDequeuedAt { get; private set; }
    public DateTimeOffset? LastAttemptCompletedAt { get; private set; }

    public Dictionary<string, string> Tags { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool CanRetry => RetryCount < MaxAttempts;

    public void MarkDequeued()
    {
        lock (_lock)
        {
            Attempt++;
            LastDequeuedAt = DateTimeOffset.UtcNow;
        }
    }

    public void MarkAttemptCompleted(bool success)
    {
        lock (_lock)
        {
            LastAttemptCompletedAt = DateTimeOffset.UtcNow;
            if (success)
            {
                ConsecutiveFailures = 0;
            }
            else
            {
                ConsecutiveFailures++;
            }
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
        => $"DocumentWorkItem {{ FileId = {FileId}, Attempt = {Attempt}, RetryCount = {RetryCount}, TraceId = {TraceId} }}";
}

public sealed class DocumentPipelineQueueOptions
{
    public int WorkerBatchSize { get; set; } = 4;
    public int MaxRetryCount { get; set; } = 5;
    public double BackoffMultiplier { get; set; } = 2.0;
    public TimeSpan InitialRetryDelay { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(5);
    public bool UseJitter { get; set; } = true;
}

public interface IDocumentPipelineQueue
{
    ValueTask EnqueueAsync(DocumentWorkItem workItem, CancellationToken ct = default);
    ValueTask<IReadOnlyList<DocumentWorkItem>> DequeueBatchAsync(int maxItems, CancellationToken ct = default);
    ValueTask<bool> ScheduleRetryAsync(DocumentWorkItem workItem, Exception reason, CancellationToken ct = default);
}

public sealed class DocumentPipelineQueue : IDocumentPipelineQueue
{
    private readonly Channel<DocumentWorkItem> _channel;
    private readonly ILogger<DocumentPipelineQueue> _logger;
    private readonly IOptionsMonitor<DocumentPipelineQueueOptions> _options;
    private readonly Random _random = new();

    public DocumentPipelineQueue(
        ILogger<DocumentPipelineQueue> logger,
        IOptionsMonitor<DocumentPipelineQueueOptions> options)
    {
        _logger = logger;
        _options = options;
        var channelOptions = new BoundedChannelOptions(capacity: 500)
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
        var opts = _options.CurrentValue;
        workItem.MaxAttempts = Math.Max(opts.MaxRetryCount, workItem.MaxAttempts);
        _logger.LogDebug("Queueing document {DocumentId} for processing (trace: {TraceId})", workItem.FileId, workItem.TraceId);
        await _channel.Writer.WriteAsync(workItem, ct);
    }

    public async ValueTask<IReadOnlyList<DocumentWorkItem>> DequeueBatchAsync(int maxItems, CancellationToken ct = default)
    {
        if (maxItems <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxItems), "Batch size must be greater than zero.");
        }

        var items = new List<DocumentWorkItem>(maxItems);

        while (!ct.IsCancellationRequested && items.Count < maxItems)
        {
            DocumentWorkItem? item = null;
            if (items.Count == 0)
            {
                try
                {
                    item = await _channel.Reader.ReadAsync(ct);
                }
                catch (ChannelClosedException)
                {
                    break;
                }
            }
            else if (_channel.Reader.TryRead(out item))
            {
                // already populated
            }

            if (item == null)
            {
                break;
            }

            item.MarkDequeued();
            items.Add(item);
        }

        return items;
    }

    public async ValueTask<bool> ScheduleRetryAsync(DocumentWorkItem workItem, Exception reason, CancellationToken ct = default)
    {
        var attempt = workItem.RegisterRetry();
        var opts = _options.CurrentValue;
        if (attempt > opts.MaxRetryCount)
        {
            _logger.LogWarning(
                reason,
                "Dropping document {DocumentId} after exhausting {RetryCount} retries (trace: {TraceId})",
                workItem.FileId,
                opts.MaxRetryCount,
                workItem.TraceId);
            return false;
        }

        var delay = CalculateBackoffDelay(attempt - 1, opts);
        _logger.LogInformation(
            reason,
            "Scheduling retry {RetryCount}/{MaxRetry} for document {DocumentId} in {Delay} (trace: {TraceId})",
            attempt,
            opts.MaxRetryCount,
            workItem.FileId,
            delay,
            workItem.TraceId);

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, ct);
                await EnqueueAsync(workItem, ct);
            }
            catch (OperationCanceledException)
            {
                // shutting down; ignore
            }
        }, CancellationToken.None);

        return true;
    }

    private TimeSpan CalculateBackoffDelay(int retryNumber, DocumentPipelineQueueOptions opts)
    {
        var baseDelay = opts.InitialRetryDelay;
        if (retryNumber <= 0)
        {
            return ApplyJitter(baseDelay, opts);
        }

        var multiplier = Math.Pow(opts.BackoffMultiplier, retryNumber);
        var delay = TimeSpan.FromMilliseconds(baseDelay.TotalMilliseconds * multiplier);
        if (delay > opts.MaxRetryDelay)
        {
            delay = opts.MaxRetryDelay;
        }

        return ApplyJitter(delay, opts);
    }

    private TimeSpan ApplyJitter(TimeSpan delay, DocumentPipelineQueueOptions opts)
    {
        if (!opts.UseJitter || delay <= TimeSpan.Zero)
        {
            return delay;
        }

        lock (_random)
        {
            var jitterFactor = _random.NextDouble() * 0.3 + 0.85; // +/-15%
            var jittered = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * jitterFactor);
            return jittered <= opts.MaxRetryDelay ? jittered : opts.MaxRetryDelay;
        }
    }
}
