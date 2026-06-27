using Microsoft.Extensions.Logging;
using S6.SnapVault.Models;
using Koan.Data.Core;

namespace S6.SnapVault.Services;

/// <summary>
/// Production monitoring service for embedding generation telemetry and cost tracking.
/// Tracks success rate, latency, token usage, and daily embedding volumes.
/// </summary>
public class EmbeddingMonitoringService
{
    private readonly ILogger<EmbeddingMonitoringService> _logger;

    // In-memory metrics (production would use distributed cache or metrics service)
    private static long _totalEmbeddingsGenerated = 0;
    private static long _totalEmbeddingsFailed = 0;
    private static long _totalTokensUsed = 0;
    private static readonly Dictionary<string, EmbeddingDailyStats> _dailyStats = new();

    public EmbeddingMonitoringService(ILogger<EmbeddingMonitoringService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Record successful embedding generation
    /// </summary>
    public void RecordSuccess(string entityType, int tokensUsed, long latencyMs)
    {
        Interlocked.Increment(ref _totalEmbeddingsGenerated);
        Interlocked.Add(ref _totalTokensUsed, tokensUsed);

        var today = DateTime.UtcNow.Date.ToString("yyyy-MM-dd");
        lock (_dailyStats)
        {
            if (!_dailyStats.ContainsKey(today))
            {
                _dailyStats[today] = new EmbeddingDailyStats { Date = today };
            }

            var stats = _dailyStats[today];
            stats.SuccessCount++;
            stats.TotalTokens += tokensUsed;
            stats.TotalLatencyMs += latencyMs;
        }

        _logger.LogDebug(
            "Embedding generated: entity={EntityType}, tokens={Tokens}, latency={Latency}ms",
            entityType, tokensUsed, latencyMs);
    }

    /// <summary>
    /// Record failed embedding generation
    /// </summary>
    public void RecordFailure(string entityType, Exception ex)
    {
        Interlocked.Increment(ref _totalEmbeddingsFailed);

        var today = DateTime.UtcNow.Date.ToString("yyyy-MM-dd");
        lock (_dailyStats)
        {
            if (!_dailyStats.ContainsKey(today))
            {
                _dailyStats[today] = new EmbeddingDailyStats { Date = today };
            }

            _dailyStats[today].FailureCount++;
        }

        _logger.LogError(ex,
            "Embedding generation failed: entity={EntityType}, error={ErrorMessage}",
            entityType, ex.Message);
    }

    /// <summary>
    /// Record truncation warning (when embedding text exceeds MaxTokens)
    /// </summary>
    public void RecordTruncation(string entityType, int originalTokens, int maxTokens)
    {
        _logger.LogWarning(
            "Embedding text truncated: entity={EntityType}, original={OriginalTokens}, max={MaxTokens}, loss={LossPercent}%",
            entityType, originalTokens, maxTokens, (int)((originalTokens - maxTokens) / (double)originalTokens * 100));
    }

    /// <summary>
    /// Get current metrics snapshot
    /// </summary>
    public EmbeddingMetrics GetMetrics()
    {
        var total = _totalEmbeddingsGenerated + _totalEmbeddingsFailed;
        var successRate = total > 0 ? (_totalEmbeddingsGenerated / (double)total * 100) : 0;

        return new EmbeddingMetrics
        {
            TotalGenerated = _totalEmbeddingsGenerated,
            TotalFailed = _totalEmbeddingsFailed,
            SuccessRate = successRate,
            TotalTokensUsed = _totalTokensUsed,
            EstimatedCost = CalculateEstimatedCost(_totalTokensUsed)
        };
    }

    /// <summary>
    /// Get daily statistics
    /// </summary>
    public List<EmbeddingDailyStats> GetDailyStats(int days = 7)
    {
        lock (_dailyStats)
        {
            return _dailyStats.Values
                .OrderByDescending(s => s.Date)
                .Take(days)
                .ToList();
        }
    }

    /// <summary>
    /// Get today's statistics
    /// </summary>
    public EmbeddingDailyStats GetTodayStats()
    {
        var today = DateTime.UtcNow.Date.ToString("yyyy-MM-dd");
        lock (_dailyStats)
        {
            if (_dailyStats.TryGetValue(today, out var stats))
            {
                return stats;
            }

            return new EmbeddingDailyStats { Date = today };
        }
    }

    /// <summary>
    /// Calculate estimated cost based on token usage
    /// Assumes OpenAI text-embedding-3-large pricing: $0.13 per 1M tokens
    /// Adjust for your actual provider and model pricing
    /// </summary>
    private static double CalculateEstimatedCost(long totalTokens)
    {
        // Example pricing (adjust based on your provider)
        const double costPerMillionTokens = 0.13; // OpenAI text-embedding-3-large
        return (totalTokens / 1_000_000.0) * costPerMillionTokens;
    }

    /// <summary>
    /// Health check: Verify embedding service is responsive
    /// </summary>
    public async Task<bool> HealthCheck(CancellationToken ct = default)
    {
        try
        {
            // Simple test embedding
            var testText = "health check test";
            var embedding = await Koan.AI.Client.Embed(testText, ct);

            return embedding != null && embedding.Length > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Embedding health check failed");
            return false;
        }
    }

    /// <summary>
    /// Alert if metrics exceed thresholds
    /// </summary>
    public void CheckAlerts()
    {
        var metrics = GetMetrics();

        // Alert: High failure rate
        if (metrics.SuccessRate < 95.0 && (metrics.TotalGenerated + metrics.TotalFailed) > 100)
        {
            _logger.LogWarning(
                "⚠️  ALERT: Embedding success rate below 95%: {SuccessRate:F2}% ({Failed} failures out of {Total} attempts)",
                metrics.SuccessRate, metrics.TotalFailed, metrics.TotalGenerated + metrics.TotalFailed);
        }

        // Alert: High daily cost
        var today = GetTodayStats();
        var dailyCost = CalculateEstimatedCost(today.TotalTokens);
        if (dailyCost > 10.0) // $10/day threshold
        {
            _logger.LogWarning(
                "⚠️  ALERT: Daily embedding cost exceeds $10: ${Cost:F2} ({Tokens:N0} tokens)",
                dailyCost, today.TotalTokens);
        }

        // Alert: High latency
        if (today.SuccessCount > 0)
        {
            var avgLatency = today.TotalLatencyMs / today.SuccessCount;
            if (avgLatency > 5000) // 5 second average
            {
                _logger.LogWarning(
                    "⚠️  ALERT: Average embedding latency exceeds 5s: {AvgLatency}ms",
                    avgLatency);
            }
        }
    }
}

/// <summary>
/// Real-time embedding metrics
/// </summary>
public class EmbeddingMetrics
{
    public long TotalGenerated { get; set; }
    public long TotalFailed { get; set; }
    public double SuccessRate { get; set; }
    public long TotalTokensUsed { get; set; }
    public double EstimatedCost { get; set; }
}

/// <summary>
/// Daily embedding statistics
/// </summary>
public class EmbeddingDailyStats
{
    public string Date { get; set; } = "";
    public long SuccessCount { get; set; }
    public long FailureCount { get; set; }
    public long TotalTokens { get; set; }
    public long TotalLatencyMs { get; set; }

    public double SuccessRate =>
        (SuccessCount + FailureCount) > 0
            ? (SuccessCount / (double)(SuccessCount + FailureCount) * 100)
            : 0;

    public long AverageLatencyMs =>
        SuccessCount > 0 ? TotalLatencyMs / SuccessCount : 0;

    public double EstimatedCost =>
        (TotalTokens / 1_000_000.0) * 0.13; // Adjust pricing as needed
}
