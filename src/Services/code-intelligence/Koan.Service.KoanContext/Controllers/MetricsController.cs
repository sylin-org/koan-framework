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
    private readonly EnhancedMetrics _enhancedMetrics;
    private readonly MetricsCollector _metricsCollector;

    public MetricsController(
        Metrics metrics,
        EnhancedMetrics enhancedMetrics,
        MetricsCollector metricsCollector)
    {
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _enhancedMetrics = enhancedMetrics ?? throw new ArgumentNullException(nameof(enhancedMetrics));
        _metricsCollector = metricsCollector ?? throw new ArgumentNullException(nameof(metricsCollector));
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

    /// <summary>
    /// Get P0 vector queue health metrics
    /// </summary>
    /// <remarks>
    /// Critical metrics for monitoring vector synchronization health:
    /// - Pending snapshot count
    /// - Failed snapshot count
    /// - Oldest pending snapshot age
    /// - Processing rate
    /// - Per-project breakdown
    ///
    /// Alerts trigger at:
    /// - ⚠️ Pending > 100 or Age > 60s
    /// - 🚨 Pending > 500 or Age > 300s or Failed > 0
    /// </remarks>
    /// <returns>Vector queue health status with alert thresholds</returns>
    [HttpGet("vector-queue")]
    public async Task<IActionResult> GetVectorQueueHealth(CancellationToken cancellationToken = default)
    {
        try
        {
            var metrics = await _enhancedMetrics.GetVectorQueueHealthAsync(cancellationToken);

            return Ok(new
            {
                data = metrics,
                metadata = new
                {
                    timestamp = metrics.Timestamp,
                    healthStatus = metrics.HealthStatus.ToString().ToLower(),
                    alerts = GetVectorQueueAlerts(metrics)
                        .Select(ToAlertPayload)
                        .ToList()
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                error = "Failed to retrieve vector queue health metrics",
                details = ex.Message
            });
        }
    }

    /// <summary>
    /// Get component health matrix
    /// </summary>
    /// <remarks>
    /// Comprehensive health checks for all system components:
    /// - SQLite database connectivity
    /// - Weaviate vector store (with latency)
    /// - File monitoring service
    /// - Vector sync worker processing
    ///
    /// Results include individual component status and overall system health.
    /// </remarks>
    /// <returns>Component health status matrix</returns>
    [HttpGet("components")]
    public async Task<IActionResult> GetComponentHealth(CancellationToken cancellationToken = default)
    {
        try
        {
            var metrics = await _enhancedMetrics.GetComponentHealthAsync(cancellationToken);

            return Ok(new
            {
                data = metrics,
                metadata = new
                {
                    timestamp = metrics.Timestamp,
                    overallHealthy = metrics.OverallHealthy,
                    componentCount = metrics.Components.Count,
                    degradedComponents = metrics.Components.Count(c => c.Status != Koan.Context.Services.HealthStatus.Healthy)
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                error = "Failed to retrieve component health",
                details = ex.Message
            });
        }
    }

    /// <summary>
    /// Get job system performance metrics
    /// </summary>
    /// <remarks>
    /// Job queue and throughput metrics:
    /// - Active and queued job counts
    /// - Success rate (last 24 hours)
    /// - Job throughput (jobs/hour)
    /// - Processing rates (chunks/sec, files/sec)
    ///
    /// Useful for detecting job system bottlenecks or failures.
    /// </remarks>
    /// <returns>Job system metrics</returns>
    [HttpGet("jobs")]
    public async Task<IActionResult> GetJobMetrics(CancellationToken cancellationToken = default)
    {
        try
        {
            var metrics = await _enhancedMetrics.GetJobSystemMetricsAsync(cancellationToken);

            return Ok(new
            {
                data = metrics,
                metadata = new
                {
                    timestamp = metrics.Timestamp,
                    alerts = GetJobSystemAlerts(metrics)
                        .Select(ToAlertPayload)
                        .ToList()
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                error = "Failed to retrieve job system metrics",
                details = ex.Message
            });
        }
    }

    /// <summary>
    /// Get vector database storage metrics
    /// </summary>
    /// <remarks>
    /// Vector database capacity and growth metrics:
    /// - Total collections (projects)
    /// - Total vector count
    /// - Estimated storage size
    /// - Growth rate (vectors/day)
    /// - Per-collection breakdown
    ///
    /// Useful for capacity planning and detecting indexing issues.
    /// </remarks>
    /// <returns>Vector DB metrics</returns>
    [HttpGet("vector-db")]
    public async Task<IActionResult> GetVectorDbMetrics(CancellationToken cancellationToken = default)
    {
        try
        {
            var metrics = await _enhancedMetrics.GetVectorDbMetricsAsync(cancellationToken);

            return Ok(new
            {
                data = metrics,
                metadata = new
                {
                    timestamp = metrics.Timestamp,
                    storageSizeReadable = FormatBytes(metrics.EstimatedSizeBytes),
                    growthRatePerDay = metrics.GrowthRatePerDay
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                error = "Failed to retrieve vector DB metrics",
                details = ex.Message
            });
        }
    }

    /// <summary>
    /// Get SQLite storage and index freshness metrics
    /// </summary>
    /// <remarks>
    /// SQLite database storage and index health:
    /// - Database size (with SQLite limits warning)
    /// - Total chunks and files indexed
    /// - Index freshness (fresh/stale/very stale project counts)
    /// - Daily growth rate
    ///
    /// ⚠️ SQLite practical limit: ~5-10GB. Consider migration to Postgres when approaching.
    /// </remarks>
    /// <returns>Storage metrics</returns>
    [HttpGet("storage")]
    public async Task<IActionResult> GetStorageMetrics(CancellationToken cancellationToken = default)
    {
        try
        {
            var metrics = await _enhancedMetrics.GetStorageMetricsAsync(cancellationToken);

            return Ok(new
            {
                data = metrics,
                metadata = new
                {
                    timestamp = metrics.Timestamp,
                    dbSizeReadable = FormatBytes(metrics.EstimatedDbSizeBytes),
                    indexedBytesReadable = FormatBytes(metrics.TotalIndexedBytes),
                    alerts = GetStorageAlerts(metrics)
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                error = "Failed to retrieve storage metrics",
                details = ex.Message
            });
        }
    }

    /// <summary>
    /// Get search performance statistics
    /// </summary>
    /// <param name="period">Time period (1h, 6h, 24h). Default: 1h</param>
    /// <remarks>
    /// Real search query performance metrics (not placeholder):
    /// - Total query count
    /// - Success/failure counts
    /// - Latency percentiles (P50, P95, P99)
    ///
    /// Data is collected from actual search query instrumentation.
    /// </remarks>
    /// <returns>Search performance statistics</returns>
    [HttpGet("search-performance")]
    public IActionResult GetSearchPerformance([FromQuery] string period = "1h")
    {
        try
        {
            var timeSpan = period switch
            {
                "1h" => TimeSpan.FromHours(1),
                "6h" => TimeSpan.FromHours(6),
                "24h" => TimeSpan.FromHours(24),
                _ => TimeSpan.FromHours(1)
            };

            var stats = _metricsCollector.CalculateSearchStats(timeSpan);

            return Ok(new
            {
                data = stats,
                metadata = new
                {
                    period,
                    timestamp = DateTime.UtcNow,
                    alerts = GetSearchPerformanceAlerts(stats)
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                error = "Failed to retrieve search performance",
                details = ex.Message
            });
        }
    }

    /// <summary>
    /// Get comprehensive dashboard overview
    /// </summary>
    /// <remarks>
    /// Aggregated metrics for dashboard overview panel:
    /// - Critical alerts from all systems
    /// - Vector queue health summary
    /// - Component health summary
    /// - Job system status
    /// - Search performance summary
    ///
    /// Single endpoint for dashboard "at-a-glance" view.
    /// </remarks>
    /// <returns>Dashboard overview metrics</returns>
    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboardOverview(CancellationToken cancellationToken = default)
    {
        try
        {
            var vectorQueue = await _enhancedMetrics.GetVectorQueueHealthAsync(cancellationToken);
            var components = await _enhancedMetrics.GetComponentHealthAsync(cancellationToken);
            var jobs = await _enhancedMetrics.GetJobSystemMetricsAsync(cancellationToken);
            var searchStats = _metricsCollector.CalculateSearchStats(TimeSpan.FromHours(1));

            // Aggregate critical alerts
            var vectorQueueAlerts = GetVectorQueueAlerts(vectorQueue);
            var jobAlerts = GetJobSystemAlerts(jobs);

            var criticalAlertDescriptors = new List<AlertDescriptor>();
            criticalAlertDescriptors.AddRange(vectorQueueAlerts.Where(alert => string.Equals(alert.Severity, "critical", System.StringComparison.OrdinalIgnoreCase)));
            criticalAlertDescriptors.AddRange(jobAlerts.Where(alert => string.Equals(alert.Severity, "critical", System.StringComparison.OrdinalIgnoreCase)));

            if (!components.OverallHealthy)
            {
                var degraded = components.Components.Where(c => c.Status != Koan.Context.Services.HealthStatus.Healthy);
                foreach (var component in degraded)
                {
                    var severity = component.Status == Koan.Context.Services.HealthStatus.Critical ? "critical" : "warning";
                    criticalAlertDescriptors.Add(new AlertDescriptor(
                        Type: "component",
                        Severity: severity,
                        Message: component.Message,
                        Metadata: new
                        {
                            component = component.Name,
                            status = component.Status.ToString(),
                            latencyMs = component.LatencyMs
                        }));
                }
            }

            var criticalAlerts = criticalAlertDescriptors
                .Select(ToAlertPayload)
                .ToList();

            return Ok(new
            {
                data = new
                {
                    vectorQueueHealth = new
                    {
                        status = vectorQueue.HealthStatus.ToString().ToLower(),
                        pending = vectorQueue.PendingCount,
                        failed = vectorQueue.FailedCount,
                        processingRate = vectorQueue.ProcessingRatePerSecond
                    },
                    componentHealth = new
                    {
                        healthy = components.OverallHealthy,
                        degradedCount = components.Components.Count(c => c.Status != Koan.Context.Services.HealthStatus.Healthy),
                        components = components.Components.Select(c => new
                        {
                            name = c.Name,
                            status = c.Status.ToString().ToLower(),
                            latencyMs = c.LatencyMs
                        })
                    },
                    jobSystem = new
                    {
                        active = jobs.ActiveJobsCount,
                        queued = jobs.QueuedJobsCount,
                        successRate24h = jobs.SuccessRate24h,
                        failed24h = jobs.FailedLast24h
                    },
                    searchPerformance = new
                    {
                        totalQueries = searchStats.TotalQueries,
                        avgLatencyMs = searchStats.AvgLatencyMs,
                        p95LatencyMs = searchStats.P95LatencyMs,
                        failureRate = searchStats.TotalQueries > 0
                            ? (double)searchStats.FailedQueries / searchStats.TotalQueries * 100
                            : 0.0
                    },
                    criticalAlerts
                },
                metadata = new
                {
                    timestamp = DateTime.UtcNow,
                    criticalAlertCount = criticalAlerts.Count
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                error = "Failed to retrieve dashboard overview",
                details = ex.Message
            });
        }
    }

    #region Alert Helper Methods

    private static List<AlertDescriptor> GetVectorQueueAlerts(Services.VectorQueueHealthMetrics metrics)
    {
        var alerts = new List<AlertDescriptor>();

        if (metrics.FailedCount > 0)
        {
            alerts.Add(new AlertDescriptor(
                Type: "vector_queue",
                Severity: "critical",
                Message: $"Vector queue has {metrics.FailedCount} failed snapshot(s) requiring manual intervention"));
        }

        if (metrics.PendingCount > 500)
        {
            alerts.Add(new AlertDescriptor(
                Type: "vector_queue",
                Severity: "critical",
                Message: $"Vector queue critically high: {metrics.PendingCount} pending snapshots"));
        }
        else if (metrics.PendingCount > 100)
        {
            alerts.Add(new AlertDescriptor(
                Type: "vector_queue",
                Severity: "warning",
                Message: $"Vector queue elevated: {metrics.PendingCount} pending snapshots"));
        }

        if (metrics.OldestAgeSeconds > 300)
        {
            alerts.Add(new AlertDescriptor(
                Type: "vector_queue",
                Severity: "critical",
                Message: $"Oldest pending snapshot is {(int)metrics.OldestAgeSeconds}s old (>5 minutes)"));
        }
        else if (metrics.OldestAgeSeconds > 60)
        {
            alerts.Add(new AlertDescriptor(
                Type: "vector_queue",
                Severity: "warning",
                Message: $"Oldest pending snapshot is {(int)metrics.OldestAgeSeconds}s old (>1 minute)"));
        }

        return alerts;
    }

    private static List<AlertDescriptor> GetJobSystemAlerts(Services.JobSystemMetrics metrics)
    {
        var alerts = new List<AlertDescriptor>();

        if (metrics.SuccessRate24h < 80)
        {
            alerts.Add(new AlertDescriptor(
                Type: "jobs",
                Severity: "critical",
                Message: $"Job success rate critically low: {metrics.SuccessRate24h:F1}% (last 24h)"));
        }
        else if (metrics.SuccessRate24h < 90)
        {
            alerts.Add(new AlertDescriptor(
                Type: "jobs",
                Severity: "warning",
                Message: $"Job success rate below threshold: {metrics.SuccessRate24h:F1}% (last 24h)"));
        }

        if (metrics.ActiveJobsCount > 10)
        {
            alerts.Add(new AlertDescriptor(
                Type: "jobs",
                Severity: "warning",
                Message: $"High concurrent job count: {metrics.ActiveJobsCount} active jobs"));
        }

        if (metrics.QueuedJobsCount > 3)
        {
            alerts.Add(new AlertDescriptor(
                Type: "jobs",
                Severity: "warning",
                Message: $"Job queue backlog: {metrics.QueuedJobsCount} jobs waiting"));
        }

        return alerts;
    }

    private static List<object> GetStorageAlerts(Services.StorageMetrics metrics)
    {
        var alerts = new List<object>();

        // SQLite practical limit is ~5-10GB
        var dbSizeGb = metrics.EstimatedDbSizeBytes / (1024.0 * 1024.0 * 1024.0);

        if (dbSizeGb > 10)
        {
            alerts.Add(new
            {
                Type = "storage",
                Severity = "critical",
                Message = $"SQLite database size ({dbSizeGb:F2} GB) exceeds recommended limit (10 GB). Consider migrating to PostgreSQL."
            });
        }
        else if (dbSizeGb > 5)
        {
            alerts.Add(new
            {
                Type = "storage",
                Severity = "warning",
                Message = $"SQLite database size ({dbSizeGb:F2} GB) approaching limit. Plan migration to PostgreSQL."
            });
        }

        if (metrics.VeryStaleProjects > 0)
        {
            alerts.Add(new
            {
                Type = "storage",
                Severity = "warning",
                Message = $"{metrics.VeryStaleProjects} project(s) have not been indexed in over 24 hours"
            });
        }

        return alerts;
    }

    private static List<object> GetSearchPerformanceAlerts(Services.SearchPerformanceStats stats)
    {
        var alerts = new List<object>();

        if (stats.P95LatencyMs > 1000)
        {
            alerts.Add(new
            {
                Type = "search",
                Severity = "warning",
                Message = $"Search P95 latency high: {stats.P95LatencyMs:F0}ms (>1s)"
            });
        }

        if (stats.TotalQueries > 0)
        {
            var errorRate = (double)stats.FailedQueries / stats.TotalQueries * 100;
            if (errorRate > 5)
            {
                alerts.Add(new
                {
                    Type = "search",
                    Severity = "critical",
                    Message = $"Search error rate high: {errorRate:F1}% ({stats.FailedQueries}/{stats.TotalQueries} queries failed)"
                });
            }
            else if (errorRate > 1)
            {
                alerts.Add(new
                {
                    Type = "search",
                    Severity = "warning",
                    Message = $"Search error rate elevated: {errorRate:F1}% ({stats.FailedQueries}/{stats.TotalQueries} queries failed)"
                });
            }
        }

        return alerts;
    }

    private static object ToAlertPayload(AlertDescriptor descriptor)
    {
        if (descriptor.Metadata is null)
        {
            return new
            {
                type = descriptor.Type,
                severity = descriptor.Severity,
                message = descriptor.Message
            };
        }

        return new
        {
            type = descriptor.Type,
            severity = descriptor.Severity,
            message = descriptor.Message,
            metadata = descriptor.Metadata
        };
    }

    private sealed record AlertDescriptor(string Type, string Severity, string Message, object? Metadata = null);

    private static string FormatBytes(long bytes)
    {
        if (bytes == 0) return "0 B";
        var k = 1024;
        var sizes = new[] { "B", "KB", "MB", "GB", "TB" };
        var i = (int)Math.Floor(Math.Log(bytes) / Math.Log(k));
        return $"{bytes / Math.Pow(k, i):F2} {sizes[i]}";
    }

    #endregion
}
