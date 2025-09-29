using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Koan.Canon.Core.Services;

/// <summary>
/// Adaptive batch processor that optimizes batch sizes based on performance metrics.
/// </summary>
public class AdaptiveBatchProcessor
{
    private int _currentBatchSize = 500;
    private readonly MovingAverage _processingTime = new MovingAverage(10);
    private readonly MovingAverage _throughput = new MovingAverage(10);
    private readonly MovingAverage _memoryUsage = new MovingAverage(5);

    public Task<int> GetOptimalBatchSize()
    {
        var avgProcessingTime = _processingTime.AverageTimeSpan;
        var avgMemoryUsage = _memoryUsage.Average;

        // Increase batch size if system is performing well
        if (avgProcessingTime < TimeSpan.FromMilliseconds(500) && 
            avgMemoryUsage < GetMemoryThreshold() && 
            _currentBatchSize < 2000)
        {
            _currentBatchSize = Math.Min(_currentBatchSize * 2, 2000);
        }
        // Decrease if system is struggling
        else if (avgProcessingTime > TimeSpan.FromSeconds(5) || 
                 avgMemoryUsage > GetMemoryThreshold() * 1.2 || 
                 _currentBatchSize > 100)
        {
            _currentBatchSize = Math.Max(_currentBatchSize / 2, 100);
        }

        return Task.FromResult(_currentBatchSize);
    }

    public void RecordMetrics(TimeSpan processingTime, int recordsProcessed, double memoryUsageBytes)
    {
        _processingTime.Add(processingTime);
        if (processingTime.TotalSeconds > 0)
        {
            _throughput.Add(recordsProcessed / processingTime.TotalSeconds);
        }
        _memoryUsage.Add(memoryUsageBytes);
    }

    private double GetMemoryThreshold()
    {
        // Use 70% of available memory as threshold
        var totalMemory = GC.GetTotalMemory(false);
        return totalMemory * 0.7;
    }
}

/// <summary>
/// Simple moving average calculator for performance metrics.
/// </summary>
public class MovingAverage
{
    private readonly Queue<double> _values;
    private readonly int _windowSize;
    private double _sum;

    public MovingAverage(int windowSize)
    {
        _windowSize = windowSize;
        _values = new Queue<double>(windowSize);
    }

    public void Add(double value)
    {
        _values.Enqueue(value);
        _sum += value;

        if (_values.Count > _windowSize)
        {
            _sum -= _values.Dequeue();
        }
    }

    public void Add(TimeSpan timeSpan)
    {
        Add(timeSpan.TotalMilliseconds);
    }

    public double Average => _values.Count > 0 ? _sum / _values.Count : 0.0;
    public TimeSpan AverageTimeSpan => TimeSpan.FromMilliseconds(Average);
}
