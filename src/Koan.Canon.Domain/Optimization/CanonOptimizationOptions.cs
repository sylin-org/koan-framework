using System;

namespace Koan.Canon.Domain.Optimization;

/// <summary>
/// Configurable options influencing canon runtime optimization strategies.
/// </summary>
public sealed class CanonOptimizationOptions
{
    /// <summary>
    /// Feature switches controlling optional optimization behaviors.
    /// </summary>
    public FeaturesSettings Features { get; init; } = new();

    /// <summary>
    /// Performance tuning settings for polling, batching, and concurrency.
    /// </summary>
    public PerformanceSettings Performance { get; init; } = new();

    /// <summary>
    /// Monitoring settings governing telemetry and reporting cadence.
    /// </summary>
    public MonitoringSettings Monitoring { get; init; } = new();

    public sealed class FeaturesSettings
    {
        public bool EnableBatchOperations { get; init; } = true;
        public bool EnableParallelProcessing { get; init; } = true;
        public bool EnableAdaptiveBatching { get; init; } = true;
        public bool UseOptimizedProjectionWorker { get; init; } = true;
        public bool EnablePerformanceMonitoring { get; init; } = true;
    }

    public sealed class PerformanceSettings
    {
        public int DefaultBatchSize { get; init; } = 500;
        public int MaxBatchSize { get; init; } = 5000;
        public TimeSpan ProjectionTickInterval { get; init; } = TimeSpan.FromSeconds(5);
    }

    public sealed class MonitoringSettings
    {
        public bool Enabled { get; init; } = true;
        public TimeSpan ReportingInterval { get; init; } = TimeSpan.FromSeconds(30);
    }
}
