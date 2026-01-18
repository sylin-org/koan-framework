using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Koan.Data.AI.Telemetry;

namespace Koan.Data.AI.Health;

/// <summary>
/// Health check for embedding operations and queue processing.
/// Part of ADR AI-0020: Entity-First AI Integration and Transaction Coordination (Phase 4).
/// </summary>
/// <remarks>
/// Monitors:
/// - Queue depth and processing health
/// - Recent error rates
/// - Embedding generation performance
/// - Provider availability (if telemetry available)
///
/// Health status determination:
/// - Healthy: Error rate &lt; 5%, queue age &lt; 5 minutes
/// - Degraded: Error rate 5-20%, queue age 5-30 minutes
/// - Unhealthy: Error rate &gt; 20%, queue age &gt; 30 minutes, or no recent activity
/// </remarks>
public sealed class EmbeddingHealthCheck : IHealthCheck
{
    private readonly ILogger<EmbeddingHealthCheck> _logger;
    private readonly EmbeddingTelemetry? _telemetry;
    private readonly TimeSpan _checkPeriod = TimeSpan.FromMinutes(5);

    // Health thresholds
    private const double HealthyErrorRatePercent = 5.0;
    private const double DegradedErrorRatePercent = 20.0;
    private const double HealthyQueueAgeMinutes = 5.0;
    private const double DegradedQueueAgeMinutes = 30.0;

    public EmbeddingHealthCheck(
        ILogger<EmbeddingHealthCheck> logger,
        EmbeddingTelemetry? telemetry = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _telemetry = telemetry;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (_telemetry == null)
        {
            // Telemetry not available - report as healthy with degraded data
            return Task.FromResult(HealthCheckResult.Healthy(
                "Embedding service operational (telemetry not available)"));
        }

        try
        {
            var stats = _telemetry.CalculateStats(_checkPeriod);
            var queueMetrics = _telemetry.GetQueueMetrics(DateTime.UtcNow.Subtract(_checkPeriod)).ToList();

            var data = new Dictionary<string, object>
            {
                ["total_embeddings"] = stats.TotalEmbeddings,
                ["successful_embeddings"] = stats.SuccessfulEmbeddings,
                ["failed_embeddings"] = stats.FailedEmbeddings,
                ["avg_latency_ms"] = stats.AvgLatencyMs,
                ["p95_latency_ms"] = stats.P95LatencyMs,
                ["total_tokens"] = stats.TotalTokens,
                ["total_cost_usd"] = stats.TotalCost,
                ["check_period_minutes"] = _checkPeriod.TotalMinutes
            };

            // Calculate error rate
            var errorRate = stats.TotalEmbeddings > 0
                ? (stats.FailedEmbeddings / (double)stats.TotalEmbeddings) * 100.0
                : 0.0;

            data["error_rate_percent"] = errorRate;

            // Get queue age
            var latestQueueMetric = queueMetrics.LastOrDefault();
            if (latestQueueMetric != null)
            {
                data["queue_pending"] = latestQueueMetric.PendingCount;
                data["queue_failed"] = latestQueueMetric.FailedCount;
                data["queue_oldest_age_seconds"] = latestQueueMetric.OldestAgeSeconds;
            }

            // Determine health status
            var status = DetermineHealthStatus(stats, latestQueueMetric, errorRate);

            var message = status switch
            {
                HealthStatus.Healthy => $"Embedding service healthy ({stats.TotalEmbeddings} embeddings, {errorRate:F1}% error rate)",
                HealthStatus.Degraded => $"Embedding service degraded ({errorRate:F1}% error rate, queue age: {latestQueueMetric?.OldestAgeSeconds ?? 0:F0}s)",
                HealthStatus.Unhealthy => $"Embedding service unhealthy ({stats.FailedEmbeddings} failures, {errorRate:F1}% error rate)",
                _ => "Unknown health status"
            };

            return Task.FromResult(new HealthCheckResult(status, message, data: data));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing embedding health check");
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "Failed to perform embedding health check",
                ex));
        }
    }

    private HealthStatus DetermineHealthStatus(
        EmbeddingPerformanceStats stats,
        QueueMetricEntry? queueMetric,
        double errorRate)
    {
        // No activity in check period - degraded (might be idle, not necessarily unhealthy)
        if (stats.TotalEmbeddings == 0)
        {
            return HealthStatus.Degraded;
        }

        // High error rate - unhealthy
        if (errorRate > DegradedErrorRatePercent)
        {
            return HealthStatus.Unhealthy;
        }

        // Moderate error rate - degraded
        if (errorRate > HealthyErrorRatePercent)
        {
            return HealthStatus.Degraded;
        }

        // Check queue health if available
        if (queueMetric != null)
        {
            var queueAgeMinutes = queueMetric.OldestAgeSeconds / 60.0;

            // Very old queue items - unhealthy
            if (queueAgeMinutes > DegradedQueueAgeMinutes)
            {
                return HealthStatus.Unhealthy;
            }

            // Moderately old queue items - degraded
            if (queueAgeMinutes > HealthyQueueAgeMinutes)
            {
                return HealthStatus.Degraded;
            }

            // Too many failed items - degraded
            if (queueMetric.FailedCount > 10)
            {
                return HealthStatus.Degraded;
            }
        }

        // All checks passed
        return HealthStatus.Healthy;
    }
}
