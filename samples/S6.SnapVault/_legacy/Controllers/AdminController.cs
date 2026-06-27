using Microsoft.AspNetCore.Mvc;
using S6.SnapVault.Services;

namespace S6.SnapVault.Controllers;

/// <summary>
/// Administrative endpoints for monitoring and diagnostics
/// </summary>
[ApiController]
[Route("api/admin")]
public class AdminController : ControllerBase
{
    private readonly EmbeddingMonitoringService _embeddingMonitor;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        EmbeddingMonitoringService embeddingMonitor,
        ILogger<AdminController> logger)
    {
        _embeddingMonitor = embeddingMonitor;
        _logger = logger;
    }

    /// <summary>
    /// Get current embedding metrics (success rate, cost, latency)
    /// </summary>
    /// <returns>Real-time embedding metrics</returns>
    [HttpGet("embedding/metrics")]
    public ActionResult<EmbeddingMetrics> GetEmbeddingMetrics()
    {
        var metrics = _embeddingMonitor.GetMetrics();
        return Ok(metrics);
    }

    /// <summary>
    /// Get daily embedding statistics (last N days)
    /// </summary>
    /// <param name="days">Number of days to retrieve (default: 7)</param>
    /// <returns>Daily statistics with success rate, token usage, and costs</returns>
    [HttpGet("embedding/daily-stats")]
    public ActionResult<List<EmbeddingDailyStats>> GetDailyStats([FromQuery] int days = 7)
    {
        if (days < 1 || days > 90)
        {
            return BadRequest("Days must be between 1 and 90");
        }

        var stats = _embeddingMonitor.GetDailyStats(days);
        return Ok(stats);
    }

    /// <summary>
    /// Get today's embedding statistics
    /// </summary>
    /// <returns>Today's embedding metrics</returns>
    [HttpGet("embedding/today")]
    public ActionResult<EmbeddingDailyStats> GetTodayStats()
    {
        var stats = _embeddingMonitor.GetTodayStats();
        return Ok(stats);
    }

    /// <summary>
    /// Run alert checks and return any active alerts
    /// </summary>
    /// <returns>List of active alerts</returns>
    [HttpGet("embedding/alerts")]
    public ActionResult CheckAlerts()
    {
        _embeddingMonitor.CheckAlerts();
        return Ok(new { message = "Alert check completed. See logs for details." });
    }

    /// <summary>
    /// Health check for embedding service
    /// </summary>
    /// <returns>Service health status</returns>
    [HttpGet("embedding/health")]
    public async Task<ActionResult> GetHealth(CancellationToken ct)
    {
        var isHealthy = await _embeddingMonitor.HealthCheck(ct);

        if (isHealthy)
        {
            return Ok(new { status = "healthy", message = "Embedding service is operational" });
        }

        return StatusCode(503, new { status = "unhealthy", message = "Embedding service is not responding" });
    }

    /// <summary>
    /// Get comprehensive dashboard data
    /// </summary>
    /// <returns>Complete monitoring dashboard data</returns>
    [HttpGet("embedding/dashboard")]
    public async Task<ActionResult> GetDashboard(CancellationToken ct)
    {
        var metrics = _embeddingMonitor.GetMetrics();
        var today = _embeddingMonitor.GetTodayStats();
        var recentStats = _embeddingMonitor.GetDailyStats(7);
        var isHealthy = await _embeddingMonitor.HealthCheck(ct);

        return Ok(new
        {
            health = isHealthy ? "healthy" : "unhealthy",
            metrics = new
            {
                totalGenerated = metrics.TotalGenerated,
                totalFailed = metrics.TotalFailed,
                successRate = $"{metrics.SuccessRate:F2}%",
                totalTokens = metrics.TotalTokensUsed,
                estimatedCost = $"${metrics.EstimatedCost:F2}"
            },
            today = new
            {
                success = today.SuccessCount,
                failures = today.FailureCount,
                successRate = $"{today.SuccessRate:F2}%",
                tokens = today.TotalTokens,
                avgLatency = $"{today.AverageLatencyMs}ms",
                estimatedCost = $"${today.EstimatedCost:F2}"
            },
            recentDays = recentStats.Select(s => new
            {
                date = s.Date,
                success = s.SuccessCount,
                failures = s.FailureCount,
                successRate = $"{s.SuccessRate:F2}%",
                tokens = s.TotalTokens,
                cost = $"${s.EstimatedCost:F2}"
            })
        });
    }
}
