using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;

namespace Koan.Data.AI.Telemetry;

/// <summary>
/// Centralized metrics collection for embedding operations using OpenTelemetry-compatible System.Diagnostics.Metrics.
/// Part of ADR AI-0020: Entity-First AI Integration and Transaction Coordination (Phase 4).
/// </summary>
/// <remarks>
/// Provides real-time instrumentation for:
/// - Embedding generation performance
/// - Token usage and cost tracking
/// - Queue health and processing rates
/// - Cache hit/miss ratios
/// - Error rates and retry patterns
///
/// Metrics are exposed via the standard .NET Meters API and can be exported to
/// Prometheus, Grafana, Application Insights, etc.
/// </remarks>
public sealed class EmbeddingTelemetry : IDisposable
{
    private readonly ILogger<EmbeddingTelemetry> _logger;
    private readonly Meter _meter;

    // Embedding generation metrics
    private readonly Counter<long> _embeddingGeneratedCounter;
    private readonly Histogram<double> _embeddingLatencyHistogram;
    private readonly Counter<long> _embeddingErrorCounter;
    private readonly Histogram<long> _embeddingTokensHistogram;
    private readonly Counter<double> _embeddingCostCounter;

    // Queue metrics
    private readonly ObservableGauge<int> _queuePendingGauge;
    private readonly ObservableGauge<int> _queueFailedGauge;
    private readonly Counter<long> _queueProcessedCounter;
    private readonly Histogram<double> _queueAgeHistogram;

    // Cache metrics
    private readonly Counter<long> _cacheHitCounter;
    private readonly Counter<long> _cacheMissCounter;
    private readonly Counter<long> _cacheInvalidationCounter;

    // Batch processing metrics
    private readonly Histogram<int> _batchSizeHistogram;
    private readonly Histogram<double> _batchDurationHistogram;

    // Cost tracking by model/provider
    private readonly Counter<double> _modelCostCounter;

    // In-memory time-series storage (last 24 hours)
    private readonly ConcurrentQueue<EmbeddingMetricEntry> _embeddingMetrics = new();
    private readonly ConcurrentQueue<QueueMetricEntry> _queueMetrics = new();
    private readonly TimeSpan _retentionPeriod = TimeSpan.FromHours(24);

    // Current state for observable gauges
    private int _currentQueuePending;
    private int _currentQueueFailed;

    public EmbeddingTelemetry(ILogger<EmbeddingTelemetry> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _meter = new Meter("Koan.Data.AI.Embeddings", "1.0.0");

        // Embedding generation metrics
        _embeddingGeneratedCounter = _meter.CreateCounter<long>(
            "koan.embeddings.generated.total",
            description: "Total number of embeddings generated");

        _embeddingLatencyHistogram = _meter.CreateHistogram<double>(
            "koan.embeddings.latency",
            unit: "ms",
            description: "Embedding generation latency in milliseconds");

        _embeddingErrorCounter = _meter.CreateCounter<long>(
            "koan.embeddings.errors.total",
            description: "Total number of embedding generation errors");

        _embeddingTokensHistogram = _meter.CreateHistogram<long>(
            "koan.embeddings.tokens",
            description: "Number of tokens processed per embedding");

        _embeddingCostCounter = _meter.CreateCounter<double>(
            "koan.embeddings.cost.total",
            unit: "USD",
            description: "Total estimated cost of embedding operations");

        // Queue metrics
        _queuePendingGauge = _meter.CreateObservableGauge(
            "koan.embeddings.queue.pending",
            () => _currentQueuePending,
            description: "Number of entities awaiting embedding generation");

        _queueFailedGauge = _meter.CreateObservableGauge(
            "koan.embeddings.queue.failed",
            () => _currentQueueFailed,
            description: "Number of embedding jobs marked as failed");

        _queueProcessedCounter = _meter.CreateCounter<long>(
            "koan.embeddings.queue.processed.total",
            description: "Total number of embedding jobs processed");

        _queueAgeHistogram = _meter.CreateHistogram<double>(
            "koan.embeddings.queue.age",
            unit: "s",
            description: "Age of oldest pending embedding job in seconds");

        // Cache metrics
        _cacheHitCounter = _meter.CreateCounter<long>(
            "koan.embeddings.cache.hits.total",
            description: "Total number of embedding cache hits");

        _cacheMissCounter = _meter.CreateCounter<long>(
            "koan.embeddings.cache.misses.total",
            description: "Total number of embedding cache misses");

        _cacheInvalidationCounter = _meter.CreateCounter<long>(
            "koan.embeddings.cache.invalidations.total",
            description: "Total number of cache invalidations due to content changes");

        // Batch processing metrics
        _batchSizeHistogram = _meter.CreateHistogram<int>(
            "koan.embeddings.batch.size",
            description: "Number of embeddings processed per batch");

        _batchDurationHistogram = _meter.CreateHistogram<double>(
            "koan.embeddings.batch.duration",
            unit: "s",
            description: "Batch processing duration in seconds");

        // Model-specific cost tracking
        _modelCostCounter = _meter.CreateCounter<double>(
            "koan.embeddings.model.cost",
            unit: "USD",
            description: "Cost breakdown by model and provider");

        _logger.LogInformation("EmbeddingTelemetry initialized with {MeterName} v{Version}",
            _meter.Name, _meter.Version);
    }

    #region Embedding Generation Metrics

    /// <summary>
    /// Records an embedding generation operation.
    /// </summary>
    /// <param name="entityType">Type of entity embedded</param>
    /// <param name="model">AI model used</param>
    /// <param name="provider">AI provider (e.g., "ollama", "openai")</param>
    /// <param name="source">AI source/group name</param>
    /// <param name="latencyMs">Generation time in milliseconds</param>
    /// <param name="tokens">Number of tokens processed</param>
    /// <param name="estimatedCost">Estimated cost in USD</param>
    /// <param name="success">Whether generation succeeded</param>
    /// <param name="errorMessage">Error message if failed</param>
    public void RecordEmbeddingGeneration(
        string entityType,
        string? model,
        string? provider,
        string? source,
        double latencyMs,
        int tokens,
        double estimatedCost,
        bool success,
        string? errorMessage = null)
    {
        var tags = new TagList
        {
            { "entity_type", entityType },
            { "model", model ?? "default" },
            { "provider", provider ?? "unknown" },
            { "source", source ?? "default" },
            { "success", success.ToString().ToLower() }
        };

        _embeddingGeneratedCounter.Add(1, tags);
        _embeddingLatencyHistogram.Record(latencyMs, tags);
        _embeddingTokensHistogram.Record(tokens, tags);

        if (success)
        {
            _embeddingCostCounter.Add(estimatedCost, tags);

            // Model-specific cost tracking
            var modelTags = new TagList
            {
                { "model", model ?? "default" },
                { "provider", provider ?? "unknown" }
            };
            _modelCostCounter.Add(estimatedCost, modelTags);
        }
        else
        {
            _embeddingErrorCounter.Add(1, tags);
        }

        // Store in time-series
        var entry = new EmbeddingMetricEntry
        {
            Timestamp = DateTime.UtcNow,
            EntityType = entityType,
            Model = model,
            Provider = provider,
            Source = source,
            LatencyMs = latencyMs,
            Tokens = tokens,
            EstimatedCost = estimatedCost,
            Success = success,
            ErrorMessage = errorMessage
        };

        _embeddingMetrics.Enqueue(entry);
        CleanupOldMetrics(_embeddingMetrics);
    }

    #endregion

    #region Queue Metrics

    /// <summary>
    /// Updates current embedding queue state.
    /// </summary>
    public void UpdateQueueState(int pending, int failed, double oldestAgeSeconds)
    {
        _currentQueuePending = pending;
        _currentQueueFailed = failed;

        if (oldestAgeSeconds > 0)
        {
            _queueAgeHistogram.Record(oldestAgeSeconds);
        }

        // Store in time-series
        var entry = new QueueMetricEntry
        {
            Timestamp = DateTime.UtcNow,
            PendingCount = pending,
            FailedCount = failed,
            OldestAgeSeconds = oldestAgeSeconds
        };

        _queueMetrics.Enqueue(entry);
        CleanupOldMetrics(_queueMetrics);
    }

    /// <summary>
    /// Records queue processing results.
    /// </summary>
    public void RecordQueueProcessing(int count, bool success, string entityType)
    {
        var tags = new TagList
        {
            { "entity_type", entityType },
            { "success", success.ToString().ToLower() }
        };

        _queueProcessedCounter.Add(count, tags);
    }

    #endregion

    #region Cache Metrics

    /// <summary>
    /// Records cache hit (embedding reused due to unchanged content signature).
    /// </summary>
    public void RecordCacheHit(string entityType)
    {
        var tags = new TagList { { "entity_type", entityType } };
        _cacheHitCounter.Add(1, tags);
    }

    /// <summary>
    /// Records cache miss (new embedding generation required).
    /// </summary>
    public void RecordCacheMiss(string entityType, string reason)
    {
        var tags = new TagList
        {
            { "entity_type", entityType },
            { "reason", reason } // "new_entity", "content_changed", "version_changed"
        };
        _cacheMissCounter.Add(1, tags);
    }

    /// <summary>
    /// Records cache invalidation due to content or version change.
    /// </summary>
    public void RecordCacheInvalidation(string entityType, string reason)
    {
        var tags = new TagList
        {
            { "entity_type", entityType },
            { "reason", reason } // "content_changed", "version_upgraded"
        };
        _cacheInvalidationCounter.Add(1, tags);
    }

    #endregion

    #region Batch Processing Metrics

    /// <summary>
    /// Records batch processing operation.
    /// </summary>
    public void RecordBatchProcessing(string entityType, int batchSize, double durationSeconds)
    {
        var tags = new TagList { { "entity_type", entityType } };

        _batchSizeHistogram.Record(batchSize, tags);
        _batchDurationHistogram.Record(durationSeconds, tags);
    }

    #endregion

    #region Time-Series Queries

    /// <summary>
    /// Gets embedding metrics for a time range.
    /// </summary>
    public IEnumerable<EmbeddingMetricEntry> GetEmbeddingMetrics(DateTime? since = null)
    {
        var cutoff = since ?? DateTime.UtcNow.Subtract(_retentionPeriod);
        return _embeddingMetrics.Where(m => m.Timestamp >= cutoff).OrderBy(m => m.Timestamp);
    }

    /// <summary>
    /// Gets queue metrics for a time range.
    /// </summary>
    public IEnumerable<QueueMetricEntry> GetQueueMetrics(DateTime? since = null)
    {
        var cutoff = since ?? DateTime.UtcNow.Subtract(_retentionPeriod);
        return _queueMetrics.Where(m => m.Timestamp >= cutoff).OrderBy(m => m.Timestamp);
    }

    /// <summary>
    /// Calculates embedding performance statistics for a time period.
    /// </summary>
    public EmbeddingPerformanceStats CalculateStats(TimeSpan period, string? entityType = null)
    {
        var since = DateTime.UtcNow.Subtract(period);
        var metrics = GetEmbeddingMetrics(since);

        if (entityType != null)
        {
            metrics = metrics.Where(m => m.EntityType == entityType);
        }

        var metricsList = metrics.ToList();

        if (metricsList.Count == 0)
        {
            return new EmbeddingPerformanceStats();
        }

        var latencies = metricsList.Select(m => m.LatencyMs).OrderBy(l => l).ToList();

        return new EmbeddingPerformanceStats
        {
            TotalEmbeddings = metricsList.Count,
            SuccessfulEmbeddings = metricsList.Count(m => m.Success),
            FailedEmbeddings = metricsList.Count(m => !m.Success),
            AvgLatencyMs = latencies.Average(),
            P50LatencyMs = GetPercentile(latencies, 0.50),
            P95LatencyMs = GetPercentile(latencies, 0.95),
            P99LatencyMs = GetPercentile(latencies, 0.99),
            TotalTokens = metricsList.Sum(m => m.Tokens),
            TotalCost = metricsList.Where(m => m.Success).Sum(m => m.EstimatedCost),
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

/// <summary>
/// Base interface for time-series metric entries.
/// </summary>
public interface IMetricEntry
{
    DateTime Timestamp { get; }
}

/// <summary>
/// Metric entry for an embedding generation operation.
/// </summary>
public record EmbeddingMetricEntry : IMetricEntry
{
    public DateTime Timestamp { get; init; }
    public string EntityType { get; init; } = "";
    public string? Model { get; init; }
    public string? Provider { get; init; }
    public string? Source { get; init; }
    public double LatencyMs { get; init; }
    public int Tokens { get; init; }
    public double EstimatedCost { get; init; }
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Metric entry for embedding queue state.
/// </summary>
public record QueueMetricEntry : IMetricEntry
{
    public DateTime Timestamp { get; init; }
    public int PendingCount { get; init; }
    public int FailedCount { get; init; }
    public double OldestAgeSeconds { get; init; }
}

/// <summary>
/// Performance statistics for embedding operations.
/// </summary>
public record EmbeddingPerformanceStats
{
    public int TotalEmbeddings { get; init; }
    public int SuccessfulEmbeddings { get; init; }
    public int FailedEmbeddings { get; init; }
    public double AvgLatencyMs { get; init; }
    public double P50LatencyMs { get; init; }
    public double P95LatencyMs { get; init; }
    public double P99LatencyMs { get; init; }
    public long TotalTokens { get; init; }
    public double TotalCost { get; init; }
    public TimeSpan Period { get; init; }
}

#endregion
