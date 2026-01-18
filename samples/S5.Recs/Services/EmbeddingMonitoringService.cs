using Koan.Data.AI.Migration;
using Koan.Data.AI.Telemetry;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using S5.Recs.Models;

namespace S5.Recs.Services;

/// <summary>
/// Demonstrates embedding monitoring, cost tracking, and provider migration patterns.
/// Part of ADR AI-0020 Phase 5: Documentation & Samples.
/// </summary>
/// <remarks>
/// This service showcases:
/// - Real-time embedding metrics collection
/// - Cost monitoring and budget alerts
/// - Provider migration (Ollama → OpenAI)
/// - Embedding health monitoring
///
/// In production, you would:
/// 1. Export metrics to Prometheus/Grafana
/// 2. Set up alerts for cost thresholds
/// 3. Use migration tools for planned maintenance
/// 4. Monitor queue health for capacity planning
/// </remarks>
public class EmbeddingMonitoringService : BackgroundService
{
    private readonly ILogger<EmbeddingMonitoringService> _logger;
    private readonly EmbeddingTelemetry? _telemetry;
    private readonly TimeSpan _reportingInterval = TimeSpan.FromHours(1);
    private readonly TimeSpan _statsWindow = TimeSpan.FromDays(1);

    public EmbeddingMonitoringService(
        ILogger<EmbeddingMonitoringService> logger,
        EmbeddingTelemetry? telemetry = null)
    {
        _logger = logger;
        _telemetry = telemetry;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_telemetry == null)
        {
            _logger.LogInformation("EmbeddingTelemetry not available - monitoring disabled");
            return;
        }

        _logger.LogInformation("EmbeddingMonitoringService started - reporting every {Interval}",
            _reportingInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReportMetrics(stoppingToken);
                await Task.Delay(_reportingInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in embedding monitoring loop");
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }

        _logger.LogInformation("EmbeddingMonitoringService stopped");
    }

    /// <summary>
    /// Generates periodic embedding metrics report.
    /// In production, export these to your observability platform (Prometheus, Datadog, etc.).
    /// </summary>
    private async Task ReportMetrics(CancellationToken ct)
    {
        // Calculate statistics for the last 24 hours
        var stats = _telemetry!.CalculateStats(_statsWindow);

        if (stats.TotalEmbeddings == 0)
        {
            _logger.LogInformation("No embedding activity in the last {Window}", _statsWindow);
            return;
        }

        // Log comprehensive metrics
        _logger.LogInformation(
            """
            Embedding Metrics (Last {Window}):
            ═══════════════════════════════════════════════
            Total Embeddings: {Total:N0}
            Success Rate: {SuccessRate:P1} ({Successful:N0}/{Total:N0})
            Failed: {Failed:N0}

            Performance:
            • Avg Latency: {AvgLatency:F0}ms
            • P50 Latency: {P50Latency:F0}ms
            • P95 Latency: {P95Latency:F0}ms
            • P99 Latency: {P99Latency:F0}ms

            Cost & Usage:
            • Total Tokens: {Tokens:N0}
            • Total Cost: ${Cost:F4}
            • Avg Cost per Embedding: ${AvgCost:F6}

            Queue Health:
            {QueueMetrics}
            ═══════════════════════════════════════════════
            """,
            _statsWindow,
            stats.TotalEmbeddings,
            stats.SuccessfulEmbeddings / (double)stats.TotalEmbeddings,
            stats.SuccessfulEmbeddings,
            stats.TotalEmbeddings,
            stats.FailedEmbeddings,
            stats.AvgLatencyMs,
            stats.P50LatencyMs,
            stats.P95LatencyMs,
            stats.P99LatencyMs,
            stats.TotalTokens,
            stats.TotalCost,
            stats.TotalCost / stats.TotalEmbeddings,
            GetQueueMetrics());

        // Check cost budget (example: $1/day)
        const decimal dailyBudget = 1.00m;
        if ((decimal)stats.TotalCost > dailyBudget)
        {
            _logger.LogWarning(
                "⚠️ Daily embedding budget exceeded: ${Cost:F2} > ${Budget:F2}",
                stats.TotalCost, dailyBudget);

            // In production: Send alert, pause async processing, etc.
            // await _alerting.SendBudgetAlert(stats.TotalCost, dailyBudget);
        }

        // Check error rate (example: > 5%)
        var errorRate = stats.FailedEmbeddings / (double)stats.TotalEmbeddings;
        if (errorRate > 0.05)
        {
            _logger.LogWarning(
                "⚠️ High embedding error rate: {ErrorRate:P1} ({Failed}/{Total})",
                errorRate, stats.FailedEmbeddings, stats.TotalEmbeddings);

            // In production: Alert on-call engineer
            // await _alerting.SendErrorRateAlert(errorRate);
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Gets current queue health metrics.
    /// </summary>
    private string GetQueueMetrics()
    {
        var queueMetrics = _telemetry!.GetQueueMetrics(DateTime.UtcNow.Subtract(_statsWindow))
            .LastOrDefault();

        if (queueMetrics == null)
            return "• No queue data available";

        return $"""
            • Pending: {queueMetrics.PendingCount}
            • Failed: {queueMetrics.FailedCount}
            • Oldest Age: {TimeSpan.FromSeconds(queueMetrics.OldestAgeSeconds):hh\:mm\:ss}
            """;
    }

    /// <summary>
    /// Example: Migrate Media embeddings from Ollama to OpenAI.
    /// Run this during off-peak hours or as a planned maintenance task.
    /// </summary>
    /// <remarks>
    /// Typical use cases:
    /// - Upgrading from local Ollama to cloud OpenAI for better quality
    /// - Switching from ada-002 to text-embedding-3-large
    /// - Moving between providers due to cost/performance trade-offs
    ///
    /// This example shows zero-downtime migration:
    /// 1. New embeddings use new model (immediate)
    /// 2. Existing embeddings migrated in background
    /// 3. Search works throughout migration
    /// </remarks>
    public static async Task MigrateMediaEmbeddings(ILogger logger)
    {
        logger.LogInformation("Starting Media embedding migration: Ollama → OpenAI");

        // Migration runs in background, doesn't block API
        var result = await EmbeddingMigrator.ReEmbedAll<Media>(
            targetModel: "text-embedding-3-large",
            targetSource: "openai-prod",
            targetProvider: "openai",
            batchSize: 50,        // Process 50 at a time
            parallel: false,      // Sequential for safety
            logger: logger
        );

        logger.LogInformation(
            """
            Migration Complete:
            • Total: {Total}
            • Successful: {Successful}
            • Failed: {Failed}
            • Duration: {Duration}
            • Success Rate: {SuccessRate:P1}
            """,
            result.TotalEntities,
            result.SuccessfulEntities,
            result.FailedEntities,
            result.Duration,
            result.SuccessRate / 100.0);

        if (!result.Success)
        {
            logger.LogError("Migration failed: {Error}", result.ErrorMessage);
        }
    }

    /// <summary>
    /// Example: Export embeddings for backup or analysis.
    /// </summary>
    public static async Task ExportMediaEmbeddings(string outputPath, ILogger logger)
    {
        logger.LogInformation("Exporting Media embeddings to {Path}", outputPath);

        await EmbeddingMigrator.ExportEmbeddings<Media>(
            outputPath: outputPath,
            logger: logger
        );

        logger.LogInformation("Export complete");

        // In production: Upload to S3/Azure Blob for disaster recovery
        // await _blobStorage.UploadAsync(outputPath, "embeddings-backup");
    }

    /// <summary>
    /// Example: Clean up orphaned embedding states.
    /// Run this periodically to remove states for deleted entities.
    /// </summary>
    public static async Task CleanupOrphanedEmbeddings(ILogger logger)
    {
        logger.LogInformation("Cleaning up orphaned Media embeddings");

        var removed = await EmbeddingMigrator.CleanupOrphanedStates<Media>(logger: logger);

        logger.LogInformation("Cleanup complete: {Count} orphaned states removed", removed);
    }

    /// <summary>
    /// Example: Demonstrate model cost comparison.
    /// Use this to inform model selection decisions.
    /// </summary>
    public static void CompareModelCosts(ILogger logger)
    {
        var models = new[]
        {
            "text-embedding-3-small",
            "text-embedding-3-large",
            "text-embedding-ada-002"
        };

        logger.LogInformation("Model Cost Comparison (per 1M tokens):");

        foreach (var model in models)
        {
            var cost = EmbeddingCostEstimator.GetModelCostPerMillion(model);
            if (cost.HasValue)
            {
                logger.LogInformation("• {Model}: ${Cost:F2}", model, cost.Value);
            }
        }

        // Example calculation for 100,000 anime titles (avg 2000 tokens each)
        const int totalTokens = 100_000 * 2_000; // 200M tokens
        logger.LogInformation("\nExample: 100K anime titles (~200M tokens):");

        foreach (var model in models)
        {
            var costPerMillion = EmbeddingCostEstimator.GetModelCostPerMillion(model);
            if (costPerMillion.HasValue)
            {
                var totalCost = (totalTokens / 1_000_000.0) * (double)costPerMillion.Value;
                logger.LogInformation("• {Model}: ${Cost:F2}", model, totalCost);
            }
        }
    }
}
