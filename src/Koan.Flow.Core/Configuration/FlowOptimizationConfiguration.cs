using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Caching.Distributed;
using System;
using System.Linq;
using Koan.Flow.Core.Data;
using Koan.Flow.Core.Services;
using Koan.Flow.Core.Monitoring;

namespace Koan.Flow.Core.Configuration;

/// <summary>
/// Comprehensive configuration for Flow optimizations with feature flags and performance tuning.
/// Provides granular control over optimization features for safe deployment and A/B testing.
/// </summary>
public static class FlowOptimizationConfiguration
{
    /// <summary>
    /// Adds Flow optimizations with comprehensive configuration options.
    /// </summary>
    public static IServiceCollection AddFlowOptimizations(
        this IServiceCollection services,
        Action<FlowOptimizationOptions>? configure = null)
    {
        var options = new FlowOptimizationOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);

        // Core optimization infrastructure
        RegisterOptimizationInfrastructure(services, options);

        // Worker optimizations based on feature flags
        RegisterOptimizedWorkers(services, options);

        // Monitoring and alerting
        RegisterMonitoring(services, options);

        return services;
    }

    /// <summary>
    /// Adds Flow optimizations with environment-based configuration.
    /// </summary>
    public static IServiceCollection AddFlowOptimizationsFromEnvironment(
        this IServiceCollection services,
        string environmentPrefix = "Koan_FLOW_")
    {
        return services.AddFlowOptimizations(options =>
        {
            // Configure from environment variables
            options.Features.EnableBatchOperations = GetEnvBool($"{environmentPrefix}ENABLE_BATCH_OPERATIONS", true);
            options.Features.EnableParallelProcessing = GetEnvBool($"{environmentPrefix}ENABLE_PARALLEL_PROCESSING", true);
            options.Features.EnableAdaptiveBatching = GetEnvBool($"{environmentPrefix}ENABLE_ADAPTIVE_BATCHING", true);
            options.Features.EnablePerformanceMonitoring = GetEnvBool($"{environmentPrefix}ENABLE_MONITORING", true);

            options.Features.UseOptimizedAssociationWorker = GetEnvBool($"{environmentPrefix}USE_OPTIMIZED_ASSOCIATION", true);
            options.Features.UseOptimizedProjectionWorker = GetEnvBool($"{environmentPrefix}USE_OPTIMIZED_PROJECTION", true);

            options.Performance.DefaultBatchSize = GetEnvInt($"{environmentPrefix}DEFAULT_BATCH_SIZE", 1000);
            options.Performance.MaxBatchSize = GetEnvInt($"{environmentPrefix}MAX_BATCH_SIZE", 5000);
            options.Performance.MaxConcurrency = GetEnvInt($"{environmentPrefix}MAX_CONCURRENCY", Environment.ProcessorCount * 2);

            var monitoringMinutes = GetEnvInt($"{environmentPrefix}MONITORING_INTERVAL_MINUTES", 5);
            options.Monitoring.MetricsReportingInterval = TimeSpan.FromMinutes(monitoringMinutes);
        });
    }

    /// <summary>
    /// Configuration preset for high-throughput production environments.
    /// </summary>
    public static IServiceCollection AddFlowOptimizationsForProduction(this IServiceCollection services)
    {
        return services.AddFlowOptimizations(options =>
        {
            // Enable all optimizations for maximum performance
            options.Features.EnableBatchOperations = true;
            options.Features.EnableParallelProcessing = true;
            options.Features.EnableAdaptiveBatching = true;
            options.Features.EnablePerformanceMonitoring = true;
            options.Features.UseOptimizedAssociationWorker = true;
            options.Features.UseOptimizedProjectionWorker = true;

            // Production-tuned performance settings
            options.Performance.DefaultBatchSize = 2000;
            options.Performance.MaxBatchSize = 10000;
            options.Performance.MinBatchSize = 100;
            options.Performance.MaxConcurrency = Environment.ProcessorCount * 3;
            options.Performance.MemoryThresholdPercent = 0.8;

            // Production monitoring
            options.Monitoring.EnableMetrics = true;
            options.Monitoring.EnableDetailedLogging = false;  // Reduce log volume
            options.Monitoring.MetricsReportingInterval = TimeSpan.FromMinutes(5);
            options.Monitoring.ThroughputAlertThreshold = 50.0;  // Alert if below 50/sec
            options.Monitoring.LatencyAlertThreshold = TimeSpan.FromSeconds(30);
        });
    }

    /// <summary>
    /// Configuration preset for development environments with detailed monitoring.
    /// </summary>
    public static IServiceCollection AddFlowOptimizationsForDevelopment(this IServiceCollection services)
    {
        return services.AddFlowOptimizations(options =>
        {
            // Enable optimizations but with conservative settings
            options.Features.EnableBatchOperations = true;
            options.Features.EnableParallelProcessing = true;
            options.Features.EnableAdaptiveBatching = true;
            options.Features.EnablePerformanceMonitoring = true;

            // Gradual worker rollout for development
            options.Features.UseOptimizedAssociationWorker = true;
            options.Features.UseOptimizedProjectionWorker = false;  // Enable manually

            // Conservative performance settings for development
            options.Performance.DefaultBatchSize = 500;
            options.Performance.MaxBatchSize = 2000;
            options.Performance.MaxConcurrency = Math.Max(Environment.ProcessorCount, 2);

            // Detailed monitoring for development
            options.Monitoring.EnableMetrics = true;
            options.Monitoring.EnableDetailedLogging = true;
            options.Monitoring.MetricsReportingInterval = TimeSpan.FromMinutes(1);
        });
    }

    /// <summary>
    /// Configuration preset for testing environments with all features disabled initially.
    /// </summary>
    public static IServiceCollection AddFlowOptimizationsForTesting(this IServiceCollection services)
    {
        return services.AddFlowOptimizations(options =>
        {
            // Enable monitoring only for testing
            options.Features.EnableBatchOperations = false;
            options.Features.EnableParallelProcessing = false;
            options.Features.EnableAdaptiveBatching = false;
            options.Features.EnablePerformanceMonitoring = true;

            // Use original workers for baseline testing
            options.Features.UseOptimizedAssociationWorker = false;
            options.Features.UseOptimizedProjectionWorker = false;

            // Small batch sizes for testing
            options.Performance.DefaultBatchSize = 10;
            options.Performance.MaxBatchSize = 50;
            options.Performance.MaxConcurrency = 1;

            // Frequent monitoring for testing
            options.Monitoring.MetricsReportingInterval = TimeSpan.FromSeconds(30);
        });
    }

    // Private helper methods

    private static void RegisterOptimizationInfrastructure(IServiceCollection services, FlowOptimizationOptions options)
    {
        if (options.Features.EnableBatchOperations)
        {
            // BatchDataAccessHelper is static, no registration needed
        }

        if (options.Features.EnableAdaptiveBatching)
        {
            services.AddSingleton<AdaptiveBatchProcessor>();
        }

        // Register bulk stage transition service if any optimization is enabled
        if (options.Features.EnableBatchOperations || options.Features.EnableParallelProcessing)
        {
            services.AddSingleton(typeof(BulkStageTransitionService));
        }
    }

    private static void RegisterOptimizedWorkers(IServiceCollection services, FlowOptimizationOptions options)
    {
        if (!options.Features.EnableParallelProcessing) return;

        // Check if orchestrators are present (same logic as base Flow registration)
        if (!HasFlowOrchestrators(services)) return;

        if (options.Features.UseOptimizedAssociationWorker)
        {
            ReplaceWorker<OptimizedModelAssociationWorker>(services, "ModelAssociationWorkerHostedService");
        }

        if (options.Features.UseOptimizedProjectionWorker)
        {
            ReplaceWorker<OptimizedModelProjectionWorker>(services, "ModelProjectionWorkerHostedService");
        }
    }

    private static void RegisterMonitoring(IServiceCollection services, FlowOptimizationOptions options)
    {
        if (!options.Monitoring.EnableMetrics) return;

        services.AddSingleton<FlowPerformanceMonitor>();

        // Register monitoring as hosted service
        services.AddSingleton<IHostedService>(provider =>
        {
            var monitor = provider.GetRequiredService<FlowPerformanceMonitor>();
            return new PerformanceMonitoringHostedService(monitor, options.Monitoring);
        });

        // Add memory cache if not already registered
        if (services.All(s => s.ServiceType != typeof(IMemoryCache)))
        {
            services.AddMemoryCache(opts =>
            {
                opts.SizeLimit = 500 * 1024 * 1024; // 500MB limit
            });
        }
    }

    private static void ReplaceWorker<TOptimizedWorker>(IServiceCollection services, string originalWorkerName)
        where TOptimizedWorker : class, IHostedService
    {
        // Remove original worker
        var originalWorker = services.FirstOrDefault(s =>
            s.ServiceType == typeof(IHostedService) &&
            s.ImplementationType?.Name == originalWorkerName);

        if (originalWorker != null)
        {
            services.Remove(originalWorker);
        }

        // Add optimized worker
        services.AddSingleton<IHostedService, TOptimizedWorker>();
    }

    private static bool HasFlowOrchestrators(IServiceCollection services)
    {
        // This is a simplified check - in real implementation would need to check for FlowOrchestrator attribute
        // For now, assume orchestrators are present if any Flow services are registered
        return services.Any(s => s.ServiceType?.Namespace?.StartsWith("Koan.Flow") == true);
    }

    private static bool GetEnvBool(string key, bool defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(key);
        return bool.TryParse(value, out var result) ? result : defaultValue;
    }

    private static int GetEnvInt(string key, int defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(key);
        return int.TryParse(value, out var result) ? result : defaultValue;
    }
}

/// <summary>
/// Comprehensive configuration options for Flow optimizations.
/// </summary>
public class FlowOptimizationOptions
{
    public FeatureFlags Features { get; set; } = new();
    public PerformanceSettings Performance { get; set; } = new();
    public MonitoringSettings Monitoring { get; set; } = new();

    /// <summary>
    /// Feature flags for enabling/disabling specific optimizations.
    /// </summary>
    public class FeatureFlags
    {
        /// <summary>
        /// Enable batch database operations to replace individual calls.
        /// </summary>
        public bool EnableBatchOperations { get; set; } = true;

        /// <summary>
        /// Enable parallel processing in background workers.
        /// </summary>
        public bool EnableParallelProcessing { get; set; } = true;

        /// <summary>
        /// Enable adaptive batch sizing based on performance metrics.
        /// </summary>
        public bool EnableAdaptiveBatching { get; set; } = true;

        /// <summary>
        /// Enable comprehensive performance monitoring.
        /// </summary>
        public bool EnablePerformanceMonitoring { get; set; } = true;

        /// <summary>
        /// Replace ModelAssociationWorker with optimized version.
        /// </summary>
        public bool UseOptimizedAssociationWorker { get; set; } = true;

        /// <summary>
        /// Replace ModelProjectionWorker with optimized version.
        /// </summary>
        public bool UseOptimizedProjectionWorker { get; set; } = true;

        /// <summary>
        /// Gradual rollout percentage for batch operations (0-100).
        /// </summary>
        public int BatchOperationsRolloutPercent { get; set; } = 100;

        /// <summary>
        /// Gradual rollout percentage for parallel processing (0-100).
        /// </summary>
        public int ParallelProcessingRolloutPercent { get; set; } = 100;
    }

    /// <summary>
    /// Performance tuning parameters.
    /// </summary>
    public class PerformanceSettings
    {
        /// <summary>
        /// Default batch size for database operations.
        /// </summary>
        public int DefaultBatchSize { get; set; } = 1000;

        /// <summary>
        /// Maximum batch size (adaptive batching upper limit).
        /// </summary>
        public int MaxBatchSize { get; set; } = 5000;

        /// <summary>
        /// Minimum batch size (adaptive batching lower limit).
        /// </summary>
        public int MinBatchSize { get; set; } = 50;

        /// <summary>
        /// Maximum number of concurrent operations.
        /// </summary>
        public int MaxConcurrency { get; set; } = Environment.ProcessorCount * 2;

        /// <summary>
        /// Interval for adaptive batch size adjustments.
        /// </summary>
        public TimeSpan AdaptiveBatchingInterval { get; set; } = TimeSpan.FromMinutes(1);

        /// <summary>
        /// Memory usage threshold before triggering optimization (0.0-1.0).
        /// </summary>
        public double MemoryThresholdPercent { get; set; } = 0.8;

        /// <summary>
        /// Maximum processing time before considering operation slow.
        /// </summary>
        public TimeSpan MaxProcessingTime { get; set; } = TimeSpan.FromMinutes(5);
    }

    /// <summary>
    /// Monitoring and alerting configuration.
    /// </summary>
    public class MonitoringSettings
    {
        /// <summary>
        /// Enable performance metrics collection.
        /// </summary>
        public bool EnableMetrics { get; set; } = true;

        /// <summary>
        /// Enable detailed debug logging for optimization operations.
        /// </summary>
        public bool EnableDetailedLogging { get; set; } = false;

        /// <summary>
        /// Interval for performance metrics reporting.
        /// </summary>
        public TimeSpan MetricsReportingInterval { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Endpoint for publishing metrics (optional).
        /// </summary>
        public string MetricsEndpoint { get; set; } = "";

        /// <summary>
        /// Email addresses for performance alerts.
        /// </summary>
        public List<string> AlertRecipients { get; set; } = new();

        /// <summary>
        /// Minimum throughput threshold for alerts (entities per second).
        /// </summary>
        public double ThroughputAlertThreshold { get; set; } = 10.0;

        /// <summary>
        /// Maximum latency threshold for alerts.
        /// </summary>
        public TimeSpan LatencyAlertThreshold { get; set; } = TimeSpan.FromSeconds(30);
    }
}

/// <summary>
/// Hosted service for managing performance monitoring lifecycle.
/// </summary>
internal class PerformanceMonitoringHostedService : IHostedService
{
    private readonly FlowPerformanceMonitor _monitor;
    private readonly FlowOptimizationOptions.MonitoringSettings _settings;

    public PerformanceMonitoringHostedService(
        FlowPerformanceMonitor monitor,
        FlowOptimizationOptions.MonitoringSettings settings)
    {
        _monitor = monitor;
        _settings = settings;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_settings.EnableMetrics)
        {
            _monitor.StartPerformanceReporting(_settings.MetricsReportingInterval, cancellationToken);
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}