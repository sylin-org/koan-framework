using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Koan.Canon.Domain.Optimization;

/// <summary>
/// Lightweight performance monitor that emits periodic diagnostics for canon operations.
/// </summary>
public sealed class CanonPerformanceMonitor : IAsyncDisposable
{
    private readonly ILogger<CanonPerformanceMonitor> _logger;
    private readonly TimeSpan _reportingInterval;
    private CancellationTokenSource? _cts;

    public CanonPerformanceMonitor(ILogger<CanonPerformanceMonitor> logger, CanonOptimizationOptions options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        _reportingInterval = options.Monitoring.ReportingInterval;
    }

    public void Start(CancellationToken cancellationToken = default)
    {
        if (_cts is not null)
        {
            return;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = Task.Run(() => RunLoopAsync(_cts.Token), CancellationToken.None);
        _logger.LogInformation("[canon-monitor] started with {Interval} interval", _reportingInterval);
    }

    public void RecordMetrics(string operation, TimeSpan duration, int recordsProcessed, long memoryBytes)
    {
        var throughput = recordsProcessed / Math.Max(duration.TotalSeconds, 0.001);
        _logger.LogDebug("[canon-monitor] {Operation} processed {Records} records in {Duration}ms (throughput={Throughput:F2} rps, memory={Memory:F2} MB)",
            operation,
            recordsProcessed,
            duration.TotalMilliseconds,
            throughput,
            memoryBytes / (1024.0 * 1024.0));
    }

    public async ValueTask DisposeAsync()
    {
        if (_cts is null)
        {
            return;
        }

        _cts.Cancel();
        _cts.Dispose();
        _cts = null;
        await Task.CompletedTask;
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var gcMemory = GC.GetTotalMemory(forceFullCollection: false);
                var workingSet = Environment.WorkingSet;
                _logger.LogInformation("[canon-monitor] memory usage (GC={Gc:F2} MB, WorkingSet={WorkingSet:F2} MB)",
                    gcMemory / (1024.0 * 1024.0),
                    workingSet / (1024.0 * 1024.0));

                await Task.Delay(_reportingInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[canon-monitor] monitoring loop error");
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
            }
        }

        _logger.LogInformation("[canon-monitor] stopped");
    }
}
