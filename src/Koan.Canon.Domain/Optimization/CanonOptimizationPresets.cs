using System;

namespace Koan.Canon.Domain.Optimization;

/// <summary>
/// Helper presets that apply curated optimization defaults for common environments.
/// </summary>
public static class CanonOptimizationPresets
{
    /// <summary>
    /// Applies production-focused throughput defaults.
    /// </summary>
    public static CanonOptimizationOptions Production()
        => new()
        {
            Features = new CanonOptimizationOptions.FeaturesSettings
            {
                EnableBatchOperations = true,
                EnableParallelProcessing = true,
                EnableAdaptiveBatching = true,
                UseOptimizedProjectionWorker = true,
                EnablePerformanceMonitoring = true
            },
            Performance = new CanonOptimizationOptions.PerformanceSettings
            {
                DefaultBatchSize = 2000,
                MaxBatchSize = 10000,
                ProjectionTickInterval = TimeSpan.FromSeconds(2)
            },
            Monitoring = new CanonOptimizationOptions.MonitoringSettings
            {
                Enabled = true,
                ReportingInterval = TimeSpan.FromSeconds(15)
            }
        };

    /// <summary>
    /// Applies development defaults favoring observability over throughput.
    /// </summary>
    public static CanonOptimizationOptions Development()
        => new()
        {
            Features = new CanonOptimizationOptions.FeaturesSettings
            {
                EnableBatchOperations = true,
                EnableParallelProcessing = true,
                EnableAdaptiveBatching = true,
                UseOptimizedProjectionWorker = false,
                EnablePerformanceMonitoring = true
            },
            Performance = new CanonOptimizationOptions.PerformanceSettings
            {
                DefaultBatchSize = 500,
                MaxBatchSize = 2000,
                ProjectionTickInterval = TimeSpan.FromSeconds(5)
            },
            Monitoring = new CanonOptimizationOptions.MonitoringSettings
            {
                Enabled = true,
                ReportingInterval = TimeSpan.FromSeconds(30)
            }
        };
}
