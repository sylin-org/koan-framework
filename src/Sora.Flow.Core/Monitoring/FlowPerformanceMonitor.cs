using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Sora.Flow.Core.Monitoring;

/// <summary>
/// Performance monitoring service for Flow operations with metrics collection and reporting.
/// </summary>
public class FlowPerformanceMonitor
{
    private readonly ILogger<FlowPerformanceMonitor> _logger;
    private CancellationTokenSource? _reportingCancellation;

    public FlowPerformanceMonitor(ILogger<FlowPerformanceMonitor> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Starts performance reporting at the specified interval.
    /// </summary>
    public void StartPerformanceReporting(TimeSpan interval, CancellationToken cancellationToken = default)
    {
        _reportingCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        
        Task.Run(async () =>
        {
            _logger.LogInformation("[performance-monitor] Starting performance reporting with {Interval} interval", interval);
            
            while (!_reportingCancellation.Token.IsCancellationRequested)
            {
                try
                {
                    await ReportPerformanceMetrics();
                    await Task.Delay(interval, _reportingCancellation.Token);
                }
                catch (OperationCanceledException) when (_reportingCancellation.Token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[performance-monitor] Error during performance reporting");
                    await Task.Delay(TimeSpan.FromMinutes(1), _reportingCancellation.Token);
                }
            }
            
            _logger.LogInformation("[performance-monitor] Performance reporting stopped");
        }, _reportingCancellation.Token);
    }

    /// <summary>
    /// Records performance metrics for a Flow operation.
    /// </summary>
    public void RecordMetrics(string operation, TimeSpan duration, int recordsProcessed, long memoryUsage)
    {
        var throughput = recordsProcessed / Math.Max(duration.TotalSeconds, 0.001);
        
        _logger.LogDebug("[performance-monitor] {Operation}: {Records} records in {Duration}ms, throughput: {Throughput:F2} records/sec, memory: {Memory:F2} MB",
            operation, recordsProcessed, duration.TotalMilliseconds, throughput, memoryUsage / (1024.0 * 1024.0));
    }

    /// <summary>
    /// Generates and logs performance report.
    /// </summary>
    private async Task ReportPerformanceMetrics()
    {
        try
        {
            var totalMemory = GC.GetTotalMemory(false);
            var workingSet = Environment.WorkingSet;
            
            _logger.LogInformation("[performance-monitor] Memory usage - GC: {GcMemory:F2} MB, Working Set: {WorkingSet:F2} MB",
                totalMemory / (1024.0 * 1024.0), workingSet / (1024.0 * 1024.0));
                
            // Force a quick GC collection to free up any unreferenced objects
            if (totalMemory > 500 * 1024 * 1024) // If using more than 500MB
            {
                GC.Collect(0, GCCollectionMode.Optimized);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[performance-monitor] Error collecting performance metrics");
        }
    }

    /// <summary>
    /// Stops performance reporting.
    /// </summary>
    public void StopPerformanceReporting()
    {
        _reportingCancellation?.Cancel();
        _reportingCancellation?.Dispose();
        _reportingCancellation = null;
    }

    public void Dispose()
    {
        StopPerformanceReporting();
    }
}