using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Koan.Canon.Domain.Optimization;

/// <summary>
/// Adapts batch sizes based on recent throughput and memory telemetry.
/// </summary>
public sealed class AdaptiveBatchProcessor
{
    private readonly ILogger<AdaptiveBatchProcessor> _logger;
    private readonly Queue<double> _processingWindows = new();
    private readonly Queue<double> _memoryWindows = new();
    private readonly object _gate = new();

    private int _currentBatchSize;

    public AdaptiveBatchProcessor(ILogger<AdaptiveBatchProcessor> logger, CanonOptimizationOptions options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        _currentBatchSize = options.Performance.DefaultBatchSize;
        MaxBatchSize = options.Performance.MaxBatchSize;
    }

    public int MaxBatchSize { get; }

    public Task<int> GetOptimalBatchSize()
    {
        lock (_gate)
        {
            return Task.FromResult(_currentBatchSize);
        }
    }

    public Task RecordMetrics(TimeSpan processingTime, int processedRecords, double memoryBytes)
    {
        if (processingTime <= TimeSpan.Zero || processedRecords <= 0)
        {
            return Task.CompletedTask;
        }

        lock (_gate)
        {
            const int capacity = 10;
            EnqueueWindow(_processingWindows, processingTime.TotalMilliseconds, capacity);
            EnqueueWindow(_memoryWindows, memoryBytes, capacity);

            var avgProcessingMs = _processingWindows.Count > 0 ? _processingWindows.Average() : processingTime.TotalMilliseconds;
            var avgMemory = _memoryWindows.Count > 0 ? _memoryWindows.Average() : memoryBytes;
            var throughput = processedRecords / Math.Max(processingTime.TotalSeconds, 0.001);

            var next = _currentBatchSize;

            if (avgProcessingMs < 500 && avgMemory < GetMemoryThreshold() && throughput > (_currentBatchSize / 2.0))
            {
                next = Math.Min(_currentBatchSize * 2, MaxBatchSize);
            }
            else if (avgProcessingMs > 5000 || avgMemory > GetMemoryThreshold() * 1.2)
            {
                next = Math.Max(_currentBatchSize / 2, 100);
            }

            if (next != _currentBatchSize)
            {
                _logger.LogDebug("[adaptive-batch] resizing batch size from {Current} to {Next} (avgMs={AvgMs:F0}, avgMemory={AvgMem:F0} bytes, throughput={Throughput:F2} records/sec)",
                    _currentBatchSize, next, avgProcessingMs, avgMemory, throughput);
                _currentBatchSize = next;
            }
        }

        return Task.CompletedTask;
    }

    private static void EnqueueWindow(Queue<double> queue, double value, int capacity)
    {
        queue.Enqueue(value);
        if (queue.Count > capacity)
        {
            queue.Dequeue();
        }
    }

    private static double GetMemoryThreshold()
        => GC.GetGCMemoryInfo().HighMemoryLoadThresholdBytes;
}
