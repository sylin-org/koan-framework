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
    public FeaturesSettings Features { get; set; } = new();

    /// <summary>
    /// Performance tuning settings for polling, batching, and concurrency.
    /// </summary>
    public PerformanceSettings Performance { get; set; } = new();

    /// <summary>
    /// Monitoring settings governing telemetry and reporting cadence.
    /// </summary>
    public MonitoringSettings Monitoring { get; set; } = new();

    public sealed class FeaturesSettings
    {
        public bool EnableBatchOperations { get; set; } = true;
        public bool EnableParallelProcessing { get; set; } = true;
        public bool EnableAdaptiveBatching { get; set; } = true;
        public bool UseOptimizedProjectionWorker { get; set; } = true;
        public bool EnablePerformanceMonitoring { get; set; } = true;
    }

    public sealed class PerformanceSettings
    {
        public int DefaultBatchSize { get; set; } = 500;
        public int MaxBatchSize { get; set; } = 5000;
        public TimeSpan ProjectionTickInterval { get; set; } = TimeSpan.FromSeconds(5);
    }

    public sealed class MonitoringSettings
    {
        public bool Enabled { get; set; } = true;
        public TimeSpan ReportingInterval { get; set; } = TimeSpan.FromSeconds(30);
    }
}
