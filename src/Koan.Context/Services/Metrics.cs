using Koan.Context.Models;
using Microsoft.Extensions.Caching.Memory;

namespace Koan.Context.Services;

/// <summary>
/// Service for retrieving system metrics and analytics
/// </summary>
public class Metrics
{
    private readonly IMemoryCache _cache;
    private const string SummaryCacheKey = "metrics:summary";
    private const string PerformanceCacheKey = "metrics:performance";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    public Metrics(IMemoryCache cache)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    /// <summary>
    /// Get dashboard summary metrics
    /// </summary>
    public async Task<MetricsSummary> GetSummaryAsync()
    {
        return await _cache.GetOrCreateAsync(SummaryCacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;

            var now = DateTime.UtcNow;
            var todayStart = now.Date;

            // Query all projects
            var allProjects = await Project.Query(p => true);
            var projectsList = allProjects.ToList();

            // Project metrics
            var projectsByStatus = projectsList.GroupBy(p => p.Status).ToDictionary(g => g.Key, g => g.Count());
            var readyCount = projectsByStatus.GetValueOrDefault(IndexingStatus.Ready, 0);
            var indexingCount = projectsByStatus.GetValueOrDefault(IndexingStatus.Indexing, 0);
            var failedCount = projectsByStatus.GetValueOrDefault(IndexingStatus.Failed, 0);

            // Projects indexed today (as a proxy for activity)
            var projectsIndexedTodayCount = projectsList.Count(p => p.LastIndexed.HasValue && p.LastIndexed.Value >= todayStart);

            // Total chunks across all projects
            var totalChunks = projectsList.Sum(p => p.DocumentCount);

            // Chunks added today (approximate - based on projects indexed today)
            var projectsIndexedToday = projectsList.Where(p => p.LastIndexed.HasValue && p.LastIndexed.Value >= todayStart);
            var chunksAddedToday = projectsIndexedToday.Sum(p => p.DocumentCount);

            // Search metrics (placeholder - would need search log tracking)
            // For now, return mock data
            var searchesToday = 0;
            var searchesLast24h = 0;
            var searchesPerHour = 0.0;

            // Performance metrics (placeholder - would need query timing logs)
            var avgLatencyMs = 0;
            var p95LatencyMs = 0;
            var p99LatencyMs = 0;
            var latencyChangeWeek = 0.0;

            return new MetricsSummary
            {
                Projects = new ProjectMetrics
                {
                    Total = projectsList.Count,
                    Ready = readyCount,
                    Indexing = indexingCount,
                    Failed = failedCount,
                    ChangeToday = projectsIndexedTodayCount
                },
                Chunks = new ChunkMetrics
                {
                    Total = totalChunks,
                    ChangeToday = chunksAddedToday,
                    ChangeTrend = chunksAddedToday > 0 ? "up" : "stable"
                },
                Searches = new SearchMetrics
                {
                    Today = searchesToday,
                    Last24h = searchesLast24h,
                    PerHour = searchesPerHour,
                    ChangeTrend = "stable"
                },
                Performance = new PerformanceMetrics
                {
                    AvgLatencyMs = avgLatencyMs,
                    P95LatencyMs = p95LatencyMs,
                    P99LatencyMs = p99LatencyMs,
                    ChangeWeek = latencyChangeWeek
                },
                Timestamp = now
            };
        }) ?? new MetricsSummary();
    }

    /// <summary>
    /// Get performance metrics over a time period
    /// </summary>
    public async Task<PerformanceTrends> GetPerformanceMetricsAsync(string period = "24h")
    {
        var cacheKey = $"{PerformanceCacheKey}:{period}";

        return await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;

            var now = DateTime.UtcNow;
            var startTime = period switch
            {
                "1h" => now.AddHours(-1),
                "6h" => now.AddHours(-6),
                "24h" => now.AddHours(-24),
                "7d" => now.AddDays(-7),
                "30d" => now.AddDays(-30),
                _ => now.AddHours(-24)
            };

            // For now, return placeholder data
            // In a real implementation, would query search/indexing logs

            var dataPoints = new List<PerformanceDataPoint>();
            var intervalMinutes = period switch
            {
                "1h" => 5,
                "6h" => 15,
                "24h" => 60,
                "7d" => 360,
                "30d" => 1440,
                _ => 60
            };

            var currentTime = startTime;
            while (currentTime <= now)
            {
                dataPoints.Add(new PerformanceDataPoint
                {
                    Timestamp = currentTime,
                    AvgLatencyMs = 150 + Random.Shared.Next(-50, 50),
                    P95LatencyMs = 300 + Random.Shared.Next(-100, 100),
                    P99LatencyMs = 600 + Random.Shared.Next(-200, 200),
                    RequestCount = Random.Shared.Next(5, 50)
                });

                currentTime = currentTime.AddMinutes(intervalMinutes);
            }

            return new PerformanceTrends
            {
                Period = period,
                StartTime = startTime,
                EndTime = now,
                DataPoints = dataPoints,
                Timestamp = now
            };
        }) ?? new PerformanceTrends();
    }
}

/// <summary>
/// Summary metrics for dashboard
/// </summary>
public record MetricsSummary
{
    public ProjectMetrics Projects { get; init; } = new();
    public ChunkMetrics Chunks { get; init; } = new();
    public SearchMetrics Searches { get; init; } = new();
    public PerformanceMetrics Performance { get; init; } = new();
    public DateTime Timestamp { get; init; }
}

public record ProjectMetrics
{
    public int Total { get; init; }
    public int Ready { get; init; }
    public int Indexing { get; init; }
    public int Failed { get; init; }
    public int ChangeToday { get; init; }
}

public record ChunkMetrics
{
    public int Total { get; init; }
    public int ChangeToday { get; init; }
    public string ChangeTrend { get; init; } = "stable";
}

public record SearchMetrics
{
    public int Today { get; init; }
    public int Last24h { get; init; }
    public double PerHour { get; init; }
    public string ChangeTrend { get; init; } = "stable";
}

public record PerformanceMetrics
{
    public int AvgLatencyMs { get; init; }
    public int P95LatencyMs { get; init; }
    public int P99LatencyMs { get; init; }
    public double ChangeWeek { get; init; }
}

/// <summary>
/// Performance trends over time
/// </summary>
public record PerformanceTrends
{
    public string Period { get; init; } = "24h";
    public DateTime StartTime { get; init; }
    public DateTime EndTime { get; init; }
    public List<PerformanceDataPoint> DataPoints { get; init; } = new();
    public DateTime Timestamp { get; init; }
}

public record PerformanceDataPoint
{
    public DateTime Timestamp { get; init; }
    public int AvgLatencyMs { get; init; }
    public int P95LatencyMs { get; init; }
    public int P99LatencyMs { get; init; }
    public int RequestCount { get; init; }
}
