using Koan.Context.Models;
using Koan.Data.Core;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Koan.Context.Services;

/// <summary>
/// Enhanced metrics service providing P0 monitoring data for dashboard
/// </summary>
/// <remarks>
/// <para>Provides real-time metrics for:</para>
/// <list type="bullet">
/// <item><description>P0: Vector Queue Health (lag, failure count, processing rate)</description></item>
/// <item><description>P0: Component Health Matrix (SQLite, Weaviate, Workers)</description></item>
/// <item><description>Job System Health (queue depth, throughput, failure rate)</description></item>
/// <item><description>Vector DB storage and growth</description></item>
/// <item><description>SQLite storage and index freshness</description></item>
/// </list>
/// </remarks>
public class EnhancedMetrics
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<EnhancedMetrics> _logger;
    private readonly MetricsCollector _metricsCollector;

    private const string VectorQueueCacheKey = "metrics:vector_queue";
    private const string ComponentHealthCacheKey = "metrics:component_health";
    private const string JobSystemCacheKey = "metrics:job_system";
    private const string VectorDbCacheKey = "metrics:vector_db";
    private const string StorageCacheKey = "metrics:storage";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(5); // Fast refresh for P0 metrics

    public EnhancedMetrics(
        IMemoryCache cache,
        ILogger<EnhancedMetrics> logger,
        MetricsCollector metricsCollector)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metricsCollector = metricsCollector ?? throw new ArgumentNullException(nameof(metricsCollector));
    }

    #region P0 Metrics: Vector Queue Health

    /// <summary>
    /// Gets critical vector synchronization queue health metrics
    /// </summary>
    /// <remarks>
    /// Alerts:
    /// - ⚠️ Pending > 100
    /// - 🚨 Pending > 500
    /// - ⚠️ OldestAge > 60s
    /// - 🚨 OldestAge > 300s
    /// - 🚨 Failed > 0
    /// </remarks>
    public async Task<VectorQueueHealthMetrics> GetVectorQueueHealthAsync(CancellationToken cancellationToken = default)
    {
        return await _cache.GetOrCreateAsync(VectorQueueCacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;

            try
            {
                // Query pending vector snapshots
                var pendingSnapshots = await ChunkVectorState.Query(
                    state => state.State == VectorSyncState.Pending,
                    cancellationToken);

                var pendingList = pendingSnapshots.ToList();
                var pendingCount = pendingList.Count;

                // Query failed snapshots (exhausted retries)
                var failedSnapshots = await ChunkVectorState.Query(
                    state => state.State == VectorSyncState.Failed,
                    cancellationToken);

                var failedList = failedSnapshots.ToList();
                var failedCount = failedList.Count;

                // Count retrying snapshots (pending with at least one attempt)
                var retryCount = pendingList.Count(state => state.AttemptCount > 0);

                // Calculate oldest pending snapshot age based on last attempt or update
                var now = DateTime.UtcNow;
                var oldestPendingObservedAt = pendingList.Count > 0
                    ? pendingList.Min(state => state.LastAttemptAt ?? state.UpdatedAt)
                    : (DateTime?)null;
                var oldestAge = oldestPendingObservedAt.HasValue
                    ? System.Math.Max(0, (now - oldestPendingObservedAt.Value).TotalSeconds)
                    : 0.0;

                // Calculate processing rate (snapshots synchronized in last 60 seconds)
                var oneMinuteAgo = now.AddSeconds(-60);
                var recentlySynced = await ChunkVectorState.Query(
                    state => state.State == VectorSyncState.Synced &&
                             state.SyncedAt != null &&
                             state.SyncedAt >= oneMinuteAgo,
                    cancellationToken);

                var processedLastMinute = recentlySynced.Count();
                var processingRate = processedLastMinute / 60.0; // snapshots per second

                // Breakdown by project
                var failedByProject = failedList
                    .GroupBy(state => state.ProjectId)
                    .ToDictionary(g => g.Key, g => g.Count());

                var byProject = pendingList
                    .GroupBy(state => state.ProjectId)
                    .Select(group =>
                    {
                        var oldestForProject = group.Min(state => state.LastAttemptAt ?? state.UpdatedAt);
                        var oldestAgeSeconds = System.Math.Max(0, (now - oldestForProject).TotalSeconds);
                        failedByProject.TryGetValue(group.Key, out var failedForProject);

                        return new VectorQueueProjectBreakdown
                        {
                            ProjectId = group.Key,
                            PendingCount = group.Count(),
                            RetryingCount = group.Count(state => state.AttemptCount > 0),
                            FailedCount = failedForProject,
                            OldestAgeSeconds = oldestAgeSeconds
                        };
                    })
                    .OrderByDescending(p => p.PendingCount)
                    .ThenByDescending(p => p.RetryingCount)
                    .Take(10) // Top 10 projects
                    .ToList();

                // Determine health status
                var healthStatus = CalculateVectorQueueHealth(pendingCount, oldestAge, failedCount);

                // Update metrics collector
                _metricsCollector.UpdateVectorQueueState(pendingCount, failedCount, oldestAge);

                return new VectorQueueHealthMetrics
                {
                    PendingCount = pendingCount,
                    FailedCount = failedCount,
                    RetryCount = retryCount,
                    OldestAgeSeconds = oldestAge,
                    ProcessingRatePerSecond = processingRate,
                    ByProject = byProject,
                    HealthStatus = healthStatus,
                    Timestamp = now
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve vector queue health metrics");
                return new VectorQueueHealthMetrics { HealthStatus = HealthStatus.Unknown };
            }
        }) ?? new VectorQueueHealthMetrics { HealthStatus = HealthStatus.Unknown };
    }

    private static HealthStatus CalculateVectorQueueHealth(int pending, double oldestAge, int failed)
    {
        if (failed > 0) return HealthStatus.Critical;
        if (pending > 500 || oldestAge > 300) return HealthStatus.Critical;
        if (pending > 100 || oldestAge > 60) return HealthStatus.Warning;
        return HealthStatus.Healthy;
    }

    #endregion

    #region P0 Metrics: Component Health Matrix

    /// <summary>
    /// Gets comprehensive component health status
    /// </summary>
    public async Task<ComponentHealthMetrics> GetComponentHealthAsync(CancellationToken cancellationToken = default)
    {
        return await _cache.GetOrCreateAsync(ComponentHealthCacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;

            var components = new List<ComponentHealth>();

            // SQLite Health
            components.Add(await CheckSQLiteHealthAsync(cancellationToken));

            // Weaviate Health (via vector query test)
            components.Add(await CheckWeaviateHealthAsync(cancellationToken));

            // File Monitor Health
            components.Add(CheckFileMonitorHealth());

            // Vector Sync Worker Health
            components.Add(await CheckVectorSyncWorkerHealthAsync(cancellationToken));

            var overallHealthy = components.All(c => c.Status == HealthStatus.Healthy);

            return new ComponentHealthMetrics
            {
                OverallHealthy = overallHealthy,
                Components = components,
                Timestamp = DateTime.UtcNow
            };
        }) ?? new ComponentHealthMetrics { OverallHealthy = false };
    }

    private async Task<ComponentHealth> CheckSQLiteHealthAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Test database connectivity by querying projects
            var projects = await Project.Query(p => true, cancellationToken);
            var _ = projects.Any(); // Force enumeration

            return new ComponentHealth
            {
                Name = "SQLite",
                Status = HealthStatus.Healthy,
                Message = "Database connectivity OK",
                LastChecked = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SQLite health check failed");
            return new ComponentHealth
            {
                Name = "SQLite",
                Status = HealthStatus.Critical,
                Message = $"Database error: {ex.Message}",
                LastChecked = DateTime.UtcNow
            };
        }
    }

    private async Task<ComponentHealth> CheckWeaviateHealthAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Get any project to test vector query
            var projects = await Project.Query(p => p.Status == IndexingStatus.Ready, cancellationToken);
            var project = projects.FirstOrDefault();

            if (project == null)
            {
                return new ComponentHealth
                {
                    Name = "Weaviate",
                    Status = HealthStatus.Healthy,
                    Message = "No projects to test (healthy idle state)",
                    LastChecked = DateTime.UtcNow
                };
            }

            // Test vector query with small limit
            var sw = System.Diagnostics.Stopwatch.StartNew();
            using (EntityContext.Partition(project.Id))
            {
                var chunks = await Chunk.Query(c => true, cancellationToken);
                var _ = chunks.Take(1).ToList(); // Force enumeration
            }
            sw.Stop();

            return new ComponentHealth
            {
                Name = "Weaviate",
                Status = HealthStatus.Healthy,
                Message = $"Vector DB OK (latency: {sw.ElapsedMilliseconds}ms)",
                LatencyMs = sw.ElapsedMilliseconds,
                LastChecked = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Weaviate health check failed");
            return new ComponentHealth
            {
                Name = "Weaviate",
                Status = HealthStatus.Critical,
                Message = $"Vector DB error: {ex.Message}",
                LastChecked = DateTime.UtcNow
            };
        }
    }

    private ComponentHealth CheckFileMonitorHealth()
    {
        // File monitor is a hosted service - if we're here, it's running
        // In production, could check last activity timestamp
        return new ComponentHealth
        {
            Name = "File Monitor",
            Status = HealthStatus.Healthy,
            Message = "Background service running",
            LastChecked = DateTime.UtcNow
        };
    }

    private async Task<ComponentHealth> CheckVectorSyncWorkerHealthAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Check if vector sync worker is making progress
            var fiveMinutesAgo = DateTime.UtcNow.AddMinutes(-5);

            var recentSynced = await ChunkVectorState.Query(
                state => state.State == VectorSyncState.Synced &&
                         state.SyncedAt != null &&
                         state.SyncedAt >= fiveMinutesAgo,
                cancellationToken);

            var recentCount = recentSynced.Count();

            // Identify stale pending snapshots (no attempts in last 5 minutes)
            var staleSnapshots = await ChunkVectorState.Query(
                state => state.State == VectorSyncState.Pending &&
                         (state.LastAttemptAt ?? state.UpdatedAt) < fiveMinutesAgo &&
                         state.AttemptCount == 0,
                cancellationToken);

            var staleCount = staleSnapshots.Count();

            // Track failed snapshots needing intervention
            var failedSnapshots = await ChunkVectorState.Query(
                state => state.State == VectorSyncState.Failed,
                cancellationToken);

            var failedCount = failedSnapshots.Count();

            if (failedCount > 0)
            {
                return new ComponentHealth
                {
                    Name = "Vector Sync Worker",
                    Status = HealthStatus.Critical,
                    Message = $"{failedCount} snapshot(s) require manual recovery",
                    LastChecked = DateTime.UtcNow
                };
            }

            if (staleCount > 10)
            {
                return new ComponentHealth
                {
                    Name = "Vector Sync Worker",
                    Status = HealthStatus.Warning,
                    Message = $"Worker may be stalled ({staleCount} stale snapshots)",
                    LastChecked = DateTime.UtcNow
                };
            }

            return new ComponentHealth
            {
                Name = "Vector Sync Worker",
                Status = HealthStatus.Healthy,
                Message = $"Processing normally ({recentCount} snapshots last 5min)",
                LastChecked = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Vector sync worker health check failed");
            return new ComponentHealth
            {
                Name = "Vector Sync Worker",
                Status = HealthStatus.Unknown,
                Message = $"Health check error: {ex.Message}",
                LastChecked = DateTime.UtcNow
            };
        }
    }

    #endregion

    #region Job System Metrics

    /// <summary>
    /// Gets job system performance metrics
    /// </summary>
    public async Task<JobSystemMetrics> GetJobSystemMetricsAsync(CancellationToken cancellationToken = default)
    {
        return await _cache.GetOrCreateAsync(JobSystemCacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;

            try
            {
                var now = DateTime.UtcNow;
                var last24Hours = now.AddHours(-24);

                // Active jobs
                var activeJobs = await Job.Query(
                    j => j.Status == JobStatus.Planning || j.Status == JobStatus.Indexing,
                    cancellationToken);

                var activeList = activeJobs.ToList();
                var activeCount = activeList.Count;

                // Queued jobs (pending)
                var queuedJobs = await Job.Query(
                    j => j.Status == JobStatus.Pending,
                    cancellationToken);

                var queuedCount = queuedJobs.Count();

                // Jobs in last 24 hours
                var recentJobs = await Job.Query(
                    j => j.StartedAt >= last24Hours,
                    cancellationToken);

                var recentList = recentJobs.ToList();
                var completedCount = recentList.Count(j => j.Status == JobStatus.Completed);
                var failedCount = recentList.Count(j => j.Status == JobStatus.Failed);
                var totalCount = recentList.Count;

                var successRate = totalCount > 0 ? (double)completedCount / totalCount * 100 : 0.0;

                // Calculate throughput (completed jobs per hour, last 24h)
                var completedJobs = recentList.Where(j => j.Status == JobStatus.Completed).ToList();
                var throughputPerHour = completedJobs.Count / 24.0;

                // Calculate average throughput metrics
                double avgChunksPerSec = 0;
                double avgFilesPerSec = 0;

                if (completedJobs.Any())
                {
                    var totalChunks = completedJobs.Sum(j => j.ChunksCreated);
                    var totalFiles = completedJobs.Sum(j => j.ProcessedFiles);
                    var totalSeconds = completedJobs.Sum(j => j.Elapsed.TotalSeconds);

                    avgChunksPerSec = totalSeconds > 0 ? totalChunks / totalSeconds : 0;
                    avgFilesPerSec = totalSeconds > 0 ? totalFiles / totalSeconds : 0;
                }

                // Update metrics collector
                _metricsCollector.UpdateActiveJobs(activeCount);

                return new JobSystemMetrics
                {
                    ActiveJobsCount = activeCount,
                    QueuedJobsCount = queuedCount,
                    CompletedLast24h = completedCount,
                    FailedLast24h = failedCount,
                    SuccessRate24h = successRate,
                    ThroughputJobsPerHour = throughputPerHour,
                    AvgChunksPerSecond = avgChunksPerSec,
                    AvgFilesPerSecond = avgFilesPerSec,
                    Timestamp = now
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve job system metrics");
                return new JobSystemMetrics();
            }
        }) ?? new JobSystemMetrics();
    }

    #endregion

    #region Vector DB & Storage Metrics

    /// <summary>
    /// Gets vector database storage and growth metrics
    /// </summary>
    public async Task<VectorDbMetrics> GetVectorDbMetricsAsync(CancellationToken cancellationToken = default)
    {
        return await _cache.GetOrCreateAsync(VectorDbCacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30); // Slower refresh

            try
            {
                var now = DateTime.UtcNow;
                var oneDayAgo = now.AddDays(-1);

                // Get all projects (collections)
                var allProjects = await Project.All(cancellationToken);
                var projectList = allProjects.ToList();

                var collectionCount = projectList.Count;
                var totalVectors = projectList.Sum(p => p.DocumentCount);

                // Estimate storage size (vectors are typically ~1536 dimensions * 4 bytes = ~6KB per vector)
                var estimatedSizeBytes = totalVectors * 6 * 1024L;

                // Calculate growth rate (chunks added in last 24h)
                var projectsIndexedToday = projectList.Where(p =>
                    p.LastIndexed.HasValue && p.LastIndexed.Value >= oneDayAgo);

                var vectorsAddedToday = projectsIndexedToday.Sum(p => p.DocumentCount);
                var growthRatePerDay = vectorsAddedToday;

                // Per-collection breakdown (top 10 by size)
                var collections = projectList
                    .OrderByDescending(p => p.DocumentCount)
                    .Take(10)
                    .Select(p => new VectorCollectionInfo
                    {
                        ProjectId = p.Id,
                        ProjectName = p.Name,
                        VectorCount = p.DocumentCount,
                        EstimatedSizeBytes = p.DocumentCount * 6 * 1024L
                    })
                    .ToList();

                // Update metrics collector
                _metricsCollector.UpdateVectorStats(collectionCount, totalVectors);

                return new VectorDbMetrics
                {
                    CollectionCount = collectionCount,
                    TotalVectors = totalVectors,
                    EstimatedSizeBytes = estimatedSizeBytes,
                    GrowthRatePerDay = growthRatePerDay,
                    Collections = collections,
                    Timestamp = now
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve vector DB metrics");
                return new VectorDbMetrics();
            }
        }) ?? new VectorDbMetrics();
    }

    /// <summary>
    /// Gets SQLite storage and index freshness metrics
    /// </summary>
    public async Task<StorageMetrics> GetStorageMetricsAsync(CancellationToken cancellationToken = default)
    {
        return await _cache.GetOrCreateAsync(StorageCacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);

            try
            {
                var now = DateTime.UtcNow;

                // Get all projects for storage calculations
                var allProjects = await Project.All(cancellationToken);
                var projectList = allProjects.ToList();

                var totalChunks = projectList.Sum(p => p.DocumentCount);
                var totalBytes = projectList.Sum(p => p.IndexedBytes);

                // Count total indexed files across all projects
                var allFiles = await IndexedFile.All(cancellationToken);
                var totalFiles = allFiles.Count();

                // Estimate database size (rough approximation)
                // SQLite overhead + chunk metadata + indexed file records
                var estimatedDbSize = totalBytes + (totalChunks * 500L) + (totalFiles * 200L);

                // Index freshness
                var oneHourAgo = now.AddHours(-1);
                var oneDayAgo = now.AddDays(-1);

                var freshCount = projectList.Count(p => p.LastIndexed.HasValue && p.LastIndexed.Value >= oneHourAgo);
                var staleCount = projectList.Count(p => p.LastIndexed.HasValue && p.LastIndexed.Value < oneHourAgo && p.LastIndexed.Value >= oneDayAgo);
                var veryStaleCount = projectList.Count(p => p.LastIndexed.HasValue && p.LastIndexed.Value < oneDayAgo);

                // Growth trend
                var chunksAddedToday = projectList
                    .Where(p => p.LastIndexed.HasValue && p.LastIndexed.Value >= oneDayAgo)
                    .Sum(p => p.DocumentCount);

                // Update metrics collector
                _metricsCollector.UpdateStorageSize(estimatedDbSize);

                return new StorageMetrics
                {
                    EstimatedDbSizeBytes = estimatedDbSize,
                    TotalChunks = totalChunks,
                    TotalFiles = totalFiles,
                    TotalIndexedBytes = totalBytes,
                    FreshProjects = freshCount,
                    StaleProjects = staleCount,
                    VeryStaleProjects = veryStaleCount,
                    ChunksAddedToday = chunksAddedToday,
                    Timestamp = now
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve storage metrics");
                return new StorageMetrics();
            }
        }) ?? new StorageMetrics();
    }

    #endregion
}

#region Metric Models

public record class VectorQueueHealthMetrics
{
    public int PendingCount { get; init; }
    public int FailedCount { get; init; }
    public int RetryCount { get; init; }
    public double OldestAgeSeconds { get; init; }
    public double ProcessingRatePerSecond { get; init; }
    public List<VectorQueueProjectBreakdown> ByProject { get; init; } = new();
    public HealthStatus HealthStatus { get; init; }
    public DateTime Timestamp { get; init; }
}

public record class VectorQueueProjectBreakdown
{
    public string ProjectId { get; init; } = string.Empty;
    public int PendingCount { get; init; }
    public int RetryingCount { get; init; }
    public int FailedCount { get; init; }
    public double OldestAgeSeconds { get; init; }
}

public record ComponentHealthMetrics
{
    public bool OverallHealthy { get; init; }
    public List<ComponentHealth> Components { get; init; } = new();
    public DateTime Timestamp { get; init; }
}

public record ComponentHealth
{
    public string Name { get; init; } = string.Empty;
    public HealthStatus Status { get; init; }
    public string Message { get; init; } = string.Empty;
    public long? LatencyMs { get; init; }
    public DateTime LastChecked { get; init; }
}

public record JobSystemMetrics
{
    public int ActiveJobsCount { get; init; }
    public int QueuedJobsCount { get; init; }
    public int CompletedLast24h { get; init; }
    public int FailedLast24h { get; init; }
    public double SuccessRate24h { get; init; }
    public double ThroughputJobsPerHour { get; init; }
    public double AvgChunksPerSecond { get; init; }
    public double AvgFilesPerSecond { get; init; }
    public DateTime Timestamp { get; init; }
}

public record VectorDbMetrics
{
    public int CollectionCount { get; init; }
    public long TotalVectors { get; init; }
    public long EstimatedSizeBytes { get; init; }
    public int GrowthRatePerDay { get; init; }
    public List<VectorCollectionInfo> Collections { get; init; } = new();
    public DateTime Timestamp { get; init; }
}

public record VectorCollectionInfo
{
    public string ProjectId { get; init; } = string.Empty;
    public string ProjectName { get; init; } = string.Empty;
    public int VectorCount { get; init; }
    public long EstimatedSizeBytes { get; init; }
}

public record StorageMetrics
{
    public long EstimatedDbSizeBytes { get; init; }
    public int TotalChunks { get; init; }
    public int TotalFiles { get; init; }
    public long TotalIndexedBytes { get; init; }
    public int FreshProjects { get; init; }
    public int StaleProjects { get; init; }
    public int VeryStaleProjects { get; init; }
    public int ChunksAddedToday { get; init; }
    public DateTime Timestamp { get; init; }
}

public enum HealthStatus
{
    Healthy,
    Warning,
    Critical,
    Unknown
}

#endregion
