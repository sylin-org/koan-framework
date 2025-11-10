using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Koan.Context.Models;
using Microsoft.Extensions.Logging;

namespace Koan.Context.Services;

/// <summary>
/// Centralized metrics collection service using OpenTelemetry-compatible System.Diagnostics.Metrics
/// </summary>
/// <remarks>
/// Provides real-time instrumentation for:
/// - Search query performance
/// - Outbox queue health
/// - Job processing metrics
/// - Component health status
///
/// Metrics are exposed via the standard .NET Meters API and can be exported to
/// Prometheus, Grafana, Application Insights, etc.
/// </remarks>
public class MetricsCollector : IDisposable
{
    private readonly ILogger<MetricsCollector> _logger;
    private readonly Meter _meter;

    // Metrics instruments
    private readonly Counter<long> _searchQueryCounter;
    private readonly Histogram<double> _searchLatencyHistogram;
    private readonly Counter<long> _searchErrorCounter;
    private readonly ObservableGauge<int> _outboxPendingGauge;
    private readonly ObservableGauge<int> _outboxDeadLetterGauge;
    private readonly Counter<long> _outboxProcessedCounter;
    private readonly Counter<long> _outboxFailedCounter;
    private readonly Histogram<double> _outboxAgeHistogram;
    private readonly Counter<long> _jobCompletedCounter;
    private readonly Counter<long> _jobFailedCounter;
    private readonly Histogram<double> _jobDurationHistogram;
    private readonly ObservableGauge<int> _activeJobsGauge;
    private readonly ObservableGauge<int> _vectorCollectionsGauge;
    private readonly ObservableGauge<long> _vectorCountGauge;
    private readonly ObservableGauge<long> _storageSizeGauge;

    // In-memory time-series storage (last 24 hours)
    private readonly ConcurrentQueue<SearchMetricEntry> _searchMetrics = new();
    private readonly ConcurrentQueue<OutboxMetricEntry> _outboxMetrics = new();
    private readonly ConcurrentQueue<JobMetricEntry> _jobMetrics = new();
    private readonly TimeSpan _retentionPeriod = TimeSpan.FromHours(24);

    // Current state for observable gauges
    private int _currentOutboxPending;
    private int _currentOutboxDeadLetter;
    private int _currentActiveJobs;
    private int _currentVectorCollections;
    private long _currentVectorCount;
    private long _currentStorageSize;

    public MetricsCollector(ILogger<MetricsCollector> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _meter = new Meter("KoanContext", "1.0.0");

        // Search metrics
        _searchQueryCounter = _meter.CreateCounter<long>(
            "koan.search.queries.total",
            description: "Total number of search queries executed");

        _searchLatencyHistogram = _meter.CreateHistogram<double>(
            "koan.search.latency",
            unit: "ms",
            description: "Search query latency in milliseconds");

        _searchErrorCounter = _meter.CreateCounter<long>(
            "koan.search.errors.total",
            description: "Total number of search query errors");

        // Outbox metrics
        _outboxPendingGauge = _meter.CreateObservableGauge(
            "koan.outbox.pending",
            () => _currentOutboxPending,
            description: "Number of pending outbox operations");

        _outboxDeadLetterGauge = _meter.CreateObservableGauge(
            "koan.outbox.deadletter",
            () => _currentOutboxDeadLetter,
            description: "Number of operations in dead-letter queue");

        _outboxProcessedCounter = _meter.CreateCounter<long>(
            "koan.outbox.processed.total",
            description: "Total number of successfully processed outbox operations");

        _outboxFailedCounter = _meter.CreateCounter<long>(
            "koan.outbox.failed.total",
            description: "Total number of failed outbox operations");

        _outboxAgeHistogram = _meter.CreateHistogram<double>(
            "koan.outbox.age",
            unit: "s",
            description: "Age of oldest pending outbox operation in seconds");

        // Job metrics
        _jobCompletedCounter = _meter.CreateCounter<long>(
            "koan.jobs.completed.total",
            description: "Total number of completed jobs");

        _jobFailedCounter = _meter.CreateCounter<long>(
            "koan.jobs.failed.total",
            description: "Total number of failed jobs");

        _jobDurationHistogram = _meter.CreateHistogram<double>(
            "koan.jobs.duration",
            unit: "s",
            description: "Job completion duration in seconds");

        _activeJobsGauge = _meter.CreateObservableGauge(
            "koan.jobs.active",
            () => _currentActiveJobs,
            description: "Number of currently active jobs");

        // Vector DB metrics
        _vectorCollectionsGauge = _meter.CreateObservableGauge(
            "koan.vector.collections",
            () => _currentVectorCollections,
            description: "Number of vector collections");

        _vectorCountGauge = _meter.CreateObservableGauge(
            "koan.vector.count",
            () => _currentVectorCount,
            description: "Total number of vectors stored");

        // Storage metrics
        _storageSizeGauge = _meter.CreateObservableGauge(
            "koan.storage.size",
            () => _currentStorageSize,
            unit: "bytes",
            description: "Total storage size in bytes");

        _logger.LogInformation("MetricsCollector initialized with {MeterName} v{Version}",
            _meter.Name, _meter.Version);
    }

    #region Search Metrics

    /// <summary>
    /// Records a search query execution
    /// </summary>
    public void RecordSearchQuery(
        string projectId,
        string query,
        double latencyMs,
        int resultCount,
        bool success,
        string? errorMessage = null)
    {
        var tags = new TagList
        {
            { "project_id", projectId },
            { "success", success.ToString().ToLower() }
        };

        _searchQueryCounter.Add(1, tags);
        _searchLatencyHistogram.Record(latencyMs, tags);

        if (!success)
        {
            _searchErrorCounter.Add(1, tags);
        }

        // Store in time-series
        var entry = new SearchMetricEntry
        {
            Timestamp = DateTime.UtcNow,
            ProjectId = projectId,
            Query = query,
            LatencyMs = latencyMs,
            ResultCount = resultCount,
            Success = success,
            ErrorMessage = errorMessage
        };

        _searchMetrics.Enqueue(entry);
        CleanupOldMetrics(_searchMetrics);
    }

    /// <summary>
    /// Records a multi-project search query
    /// </summary>
    public void RecordMultiProjectSearch(
        List<string> projectIds,
        string query,
        double latencyMs,
        int totalResults,
        int projectCount,
        bool success)
    {
        var tags = new TagList
        {
            { "project_count", projectCount },
            { "success", success.ToString().ToLower() },
            { "type", "multi_project" }
        };

        _searchQueryCounter.Add(1, tags);
        _searchLatencyHistogram.Record(latencyMs, tags);

        if (!success)
        {
            _searchErrorCounter.Add(1, tags);
        }
    }

    #endregion

    #region Outbox Metrics

    /// <summary>
    /// Updates current outbox queue state
    /// </summary>
    public void UpdateOutboxState(int pending, int deadLetter, double oldestAgeSeconds)
    {
        _currentOutboxPending = pending;
        _currentOutboxDeadLetter = deadLetter;

        if (oldestAgeSeconds > 0)
        {
            _outboxAgeHistogram.Record(oldestAgeSeconds);
        }

        // Store in time-series
        var entry = new OutboxMetricEntry
        {
            Timestamp = DateTime.UtcNow,
            PendingCount = pending,
            DeadLetterCount = deadLetter,
            OldestAgeSeconds = oldestAgeSeconds
        };

        _outboxMetrics.Enqueue(entry);
        CleanupOldMetrics(_outboxMetrics);
    }

    /// <summary>
    /// Records outbox operation processing
    /// </summary>
    public void RecordOutboxProcessed(int count, bool success)
    {
        if (success)
        {
            _outboxProcessedCounter.Add(count);
        }
        else
        {
            _outboxFailedCounter.Add(count);
        }
    }

    #endregion

    #region Job Metrics

    /// <summary>
    /// Updates active job count
    /// </summary>
    public void UpdateActiveJobs(int count)
    {
        _currentActiveJobs = count;
    }

    /// <summary>
    /// Records job completion
    /// </summary>
    public void RecordJobCompleted(
        string jobId,
        string projectId,
        double durationSeconds,
        bool success,
        int filesProcessed,
        int chunksCreated)
    {
        var tags = new TagList
        {
            { "project_id", projectId },
            { "success", success.ToString().ToLower() }
        };

        if (success)
        {
            _jobCompletedCounter.Add(1, tags);
        }
        else
        {
            _jobFailedCounter.Add(1, tags);
        }

        _jobDurationHistogram.Record(durationSeconds, tags);

        // Store in time-series
        var entry = new JobMetricEntry
        {
            Timestamp = DateTime.UtcNow,
            JobId = jobId,
            ProjectId = projectId,
            DurationSeconds = durationSeconds,
            Success = success,
            FilesProcessed = filesProcessed,
            ChunksCreated = chunksCreated
        };

        _jobMetrics.Enqueue(entry);
        CleanupOldMetrics(_jobMetrics);
    }

    #endregion

    #region Vector & Storage Metrics

    /// <summary>
    /// Updates vector database statistics
    /// </summary>
    public void UpdateVectorStats(int collections, long totalVectors)
    {
        _currentVectorCollections = collections;
        _currentVectorCount = totalVectors;
    }

    /// <summary>
    /// Updates storage size
    /// </summary>
    public void UpdateStorageSize(long bytes)
    {
        _currentStorageSize = bytes;
    }

    #endregion

    #region Time-Series Queries

    /// <summary>
    /// Gets search metrics for a time range
    /// </summary>
    public IEnumerable<SearchMetricEntry> GetSearchMetrics(DateTime? since = null)
    {
        var cutoff = since ?? DateTime.UtcNow.Subtract(_retentionPeriod);
        return _searchMetrics.Where(m => m.Timestamp >= cutoff).OrderBy(m => m.Timestamp);
    }

    /// <summary>
    /// Gets outbox metrics for a time range
    /// </summary>
    public IEnumerable<OutboxMetricEntry> GetOutboxMetrics(DateTime? since = null)
    {
        var cutoff = since ?? DateTime.UtcNow.Subtract(_retentionPeriod);
        return _outboxMetrics.Where(m => m.Timestamp >= cutoff).OrderBy(m => m.Timestamp);
    }

    /// <summary>
    /// Gets job metrics for a time range
    /// </summary>
    public IEnumerable<JobMetricEntry> GetJobMetrics(DateTime? since = null)
    {
        var cutoff = since ?? DateTime.UtcNow.Subtract(_retentionPeriod);
        return _jobMetrics.Where(m => m.Timestamp >= cutoff).OrderBy(m => m.Timestamp);
    }

    /// <summary>
    /// Calculates search performance statistics
    /// </summary>
    public SearchPerformanceStats CalculateSearchStats(TimeSpan period)
    {
        var since = DateTime.UtcNow.Subtract(period);
        var metrics = GetSearchMetrics(since).ToList();

        if (metrics.Count == 0)
        {
            return new SearchPerformanceStats();
        }

        var latencies = metrics.Select(m => m.LatencyMs).OrderBy(l => l).ToList();

        return new SearchPerformanceStats
        {
            TotalQueries = metrics.Count,
            SuccessfulQueries = metrics.Count(m => m.Success),
            FailedQueries = metrics.Count(m => !m.Success),
            AvgLatencyMs = latencies.Average(),
            P50LatencyMs = GetPercentile(latencies, 0.50),
            P95LatencyMs = GetPercentile(latencies, 0.95),
            P99LatencyMs = GetPercentile(latencies, 0.99),
            Period = period
        };
    }

    #endregion

    #region Helper Methods

    private void CleanupOldMetrics<T>(ConcurrentQueue<T> queue) where T : IMetricEntry
    {
        var cutoff = DateTime.UtcNow.Subtract(_retentionPeriod);

        while (queue.TryPeek(out var entry) && entry.Timestamp < cutoff)
        {
            queue.TryDequeue(out _);
        }
    }

    private static double GetPercentile(List<double> sortedValues, double percentile)
    {
        if (sortedValues.Count == 0) return 0;

        var index = (int)Math.Ceiling(percentile * sortedValues.Count) - 1;
        index = Math.Max(0, Math.Min(index, sortedValues.Count - 1));

        return sortedValues[index];
    }

    #endregion

    public void Dispose()
    {
        _meter?.Dispose();
    }
}

#region Metric Entry Models

public interface IMetricEntry
{
    DateTime Timestamp { get; }
}

public record SearchMetricEntry : IMetricEntry
{
    public DateTime Timestamp { get; init; }
    public string ProjectId { get; init; } = string.Empty;
    public string Query { get; init; } = string.Empty;
    public double LatencyMs { get; init; }
    public int ResultCount { get; init; }
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
}

public record OutboxMetricEntry : IMetricEntry
{
    public DateTime Timestamp { get; init; }
    public int PendingCount { get; init; }
    public int DeadLetterCount { get; init; }
    public double OldestAgeSeconds { get; init; }
}

public record JobMetricEntry : IMetricEntry
{
    public DateTime Timestamp { get; init; }
    public string JobId { get; init; } = string.Empty;
    public string ProjectId { get; init; } = string.Empty;
    public double DurationSeconds { get; init; }
    public bool Success { get; init; }
    public int FilesProcessed { get; init; }
    public int ChunksCreated { get; init; }
}

public record SearchPerformanceStats
{
    public int TotalQueries { get; init; }
    public int SuccessfulQueries { get; init; }
    public int FailedQueries { get; init; }
    public double AvgLatencyMs { get; init; }
    public double P50LatencyMs { get; init; }
    public double P95LatencyMs { get; init; }
    public double P99LatencyMs { get; init; }
    public TimeSpan Period { get; init; }
}

#endregion
