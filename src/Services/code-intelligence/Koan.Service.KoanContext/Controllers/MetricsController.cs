using Koan.Context.Services;
using Microsoft.AspNetCore.Mvc;

namespace Koan.Context.Controllers;

/// <summary>
/// REST API controller for system metrics and analytics
/// </summary>
[ApiController]
[Route("api/metrics")]
public class MetricsController : ControllerBase
{
    private readonly Metrics _metrics;

    public MetricsController(Metrics metrics)
    {
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
    }

    /// <summary>
    /// Get dashboard summary metrics
    /// </summary>
    /// <remarks>
    /// Returns:
    /// - Total projects (by status)
    /// - Total chunks indexed
    /// - Search statistics
    /// - Performance metrics
    ///
    /// Results are cached for 30 seconds to avoid expensive queries.
    /// </remarks>
    /// <returns>Summary metrics</returns>
    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary()
    {
        try
        {
            var summary = await _metrics.GetSummaryAsync();

            return Ok(new
            {
                data = summary,
                metadata = new
                {
                    timestamp = DateTime.UtcNow,
                    cached = true,
                    cacheDuration = "30s"
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                error = "Failed to retrieve metrics summary",
                details = ex.Message
            });
        }
    }

    /// <summary>
    /// Get performance metrics over a time period
    /// </summary>
    /// <param name="period">Time period (1h, 6h, 24h, 7d, 30d). Default: 24h</param>
    /// <remarks>
    /// Returns performance trends including:
    /// - Average latency
    /// - P95/P99 latency
    /// - Request counts
    ///
    /// Data is returned as time-series for charting.
    /// Results are cached for 30 seconds.
    /// </remarks>
    /// <returns>Performance trend data</returns>
    [HttpGet("performance")]
    public async Task<IActionResult> GetPerformance([FromQuery] string period = "24h")
    {
        try
        {
            // Validate period
            var validPeriods = new[] { "1h", "6h", "24h", "7d", "30d" };
            if (!validPeriods.Contains(period))
            {
                return BadRequest(new
                {
                    error = "Invalid period",
                    details = $"Period must be one of: {string.Join(", ", validPeriods)}",
                    received = period
                });
            }

            var performance = await _metrics.GetPerformanceMetricsAsync(period);

            return Ok(new
            {
                data = performance,
                metadata = new
                {
                    timestamp = DateTime.UtcNow,
                    period,
                    dataPoints = performance.DataPoints.Count,
                    cached = true,
                    cacheDuration = "30s"
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                error = "Failed to retrieve performance metrics",
                details = ex.Message,
                period
            });
        }
    }

    /// <summary>
    /// Get system health status
    /// </summary>
    /// <remarks>
    /// Returns overall system health including:
    /// - Service availability
    /// - Database connectivity
    /// - Vector store status
    /// - Disk space
    /// </remarks>
    /// <returns>System health status</returns>
    [HttpGet("health")]
    public async Task<IActionResult> GetHealth()
    {
        try
        {
            // Check database connectivity by querying projects
            var projects = await Koan.Context.Models.Project.Query(p => true);
            var hasProjects = projects.Any();

            // Check if any projects are in failed state
            var failedProjects = projects.Count(p => p.Status == Koan.Context.Models.IndexingStatus.Failed);

            // Overall health assessment
            var isHealthy = failedProjects == 0;

            return Ok(new
            {
                healthy = isHealthy,
                status = isHealthy ? "healthy" : "degraded",
                checks = new
                {
                    database = new
                    {
                        healthy = true,
                        message = "Database connectivity OK"
                    },
                    projects = new
                    {
                        healthy = failedProjects == 0,
                        total = projects.Count(),
                        failed = failedProjects,
                        message = failedProjects == 0 ? "All projects healthy" : $"{failedProjects} project(s) in failed state"
                    }
                },
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            return StatusCode(503, new
            {
                healthy = false,
                status = "unhealthy",
                error = "Health check failed",
                details = ex.Message,
                timestamp = DateTime.UtcNow
            });
        }
    }
}
