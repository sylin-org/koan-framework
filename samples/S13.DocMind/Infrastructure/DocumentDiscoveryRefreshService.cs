using System;
using System.Threading;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using S13.DocMind.Services;

namespace S13.DocMind.Infrastructure;

public interface IDocumentDiscoveryRefreshScheduler
{
    Task EnsureRefreshAsync(string reason, CancellationToken cancellationToken);
    DocumentDiscoveryRefreshStatus Snapshot();
}

public sealed record DocumentDiscoveryRefreshStatus(
    int PendingCount,
    DateTimeOffset? LastCompletedAt,
    TimeSpan? LastDuration,
    string? LastError,
    DateTimeOffset? LastQueuedAt,
    string? LastReason,
    DateTimeOffset? LastStartedAt,
    long TotalCompleted,
    long TotalFailed,
    TimeSpan? AverageDuration,
    TimeSpan? MaxDuration);

internal sealed record DocumentDiscoveryRefreshRequest(string Reason, DateTimeOffset RequestedAt);

public sealed class DocumentDiscoveryRefreshService : BackgroundService, IDocumentDiscoveryRefreshScheduler
{
    private static readonly TimeSpan MinimumQueueSpacing = TimeSpan.FromSeconds(5);

    private readonly Channel<DocumentDiscoveryRefreshRequest> _channel;
    private readonly TimeProvider _clock;
    private readonly IDocumentDiscoveryRefresher _refresher;
    private readonly ILogger<DocumentDiscoveryRefreshService> _logger;

    private readonly object _gate = new();
    private int _pending;
    private DateTimeOffset? _lastCompletedAt;
    private TimeSpan? _lastDuration;
    private string? _lastError;
    private DateTimeOffset? _lastQueuedAt;
    private string? _lastReason;
    private DateTimeOffset? _lastStartedAt;
    private long _totalCompleted;
    private long _totalFailed;
    private TimeSpan _totalDuration = TimeSpan.Zero;
    private TimeSpan? _maxDuration;
    private DocumentDiscoveryRefreshStatus _status = new(0, null, null, null, null, null, null, 0, 0, null, null);
    private static DocumentDiscoveryRefreshStatus _latestStatus = new(0, null, null, null, null, null, null, 0, 0, null, null);

    public DocumentDiscoveryRefreshService(
        TimeProvider clock,
        IDocumentDiscoveryRefresher refresher,
        ILogger<DocumentDiscoveryRefreshService> logger)
    {
        _clock = clock;
        _refresher = refresher;
        _logger = logger;
        _channel = Channel.CreateBounded<DocumentDiscoveryRefreshRequest>(new BoundedChannelOptions(10)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });
    }

    public Task EnsureRefreshAsync(string reason, CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow();
        var request = new DocumentDiscoveryRefreshRequest(reason, now);
        lock (_gate)
        {
            if (_pending > 0 && _lastQueuedAt.HasValue && (now - _lastQueuedAt.Value) < MinimumQueueSpacing)
            {
                _lastReason = reason;
                return Task.CompletedTask;
            }

            _pending++;
            _lastQueuedAt = now;
            _lastReason = reason;
        }

        return _channel.Writer.WriteAsync(request, cancellationToken).AsTask();
    }

    public DocumentDiscoveryRefreshStatus Snapshot()
    {
        lock (_gate)
        {
            return PublishStatusLocked();
        }
    }

    public static DocumentDiscoveryRefreshStatus LatestStatus
        => Volatile.Read(ref _latestStatus);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            var started = _clock.GetUtcNow();
            try
            {
                lock (_gate)
                {
                    _lastStartedAt = started;
                    PublishStatusLocked();
                }

                await _refresher.RefreshAsync(stoppingToken).ConfigureAwait(false);
                var duration = _clock.GetUtcNow() - started;

                lock (_gate)
                {
                    _pending = Math.Max(0, _pending - 1);
                    _lastCompletedAt = _clock.GetUtcNow();
                    _lastDuration = duration;
                    _lastError = null;
                    _totalCompleted++;
                    _totalDuration += duration;
                    if (!_maxDuration.HasValue || duration > _maxDuration)
                    {
                        _maxDuration = duration;
                    }
                    PublishStatusLocked();
                }

                _logger.LogInformation("Discovery projection refreshed in {Duration} ms (reason: {Reason})", duration.TotalMilliseconds, request.Reason);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Discovery projection refresh failed");
                lock (_gate)
                {
                    _pending = Math.Max(0, _pending - 1);
                    _lastCompletedAt = _clock.GetUtcNow();
                    _lastError = ex.Message;
                    _totalFailed++;
                    PublishStatusLocked();
                }
            }
        }
    }

    private DocumentDiscoveryRefreshStatus PublishStatusLocked()
    {
        _status = new DocumentDiscoveryRefreshStatus(
            _pending,
            _lastCompletedAt,
            _lastDuration,
            _lastError,
            _lastQueuedAt,
            _lastReason,
            _lastStartedAt,
            _totalCompleted,
            _totalFailed,
            _totalCompleted > 0 ? TimeSpan.FromMilliseconds(_totalDuration.TotalMilliseconds / _totalCompleted) : null,
            _maxDuration);

        Volatile.Write(ref _latestStatus, _status);
        return _status;
    }
}
