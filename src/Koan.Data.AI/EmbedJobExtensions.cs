using Koan.Data.Abstractions;
using Koan.Data.Core;

namespace Koan.Data.AI;

/// <summary>
/// Admin commands and utilities for managing embedding jobs.
/// Part of ARCH-0070: Attribute-Driven AI Embeddings (Phase 3).
/// </summary>
public static class EmbedJobExtensions
{
    /// <summary>
    /// Retries all failed jobs for a specific entity type.
    /// Resets status to Pending and clears retry count.
    /// </summary>
    public static async Task<int> RetryFailed<TEntity>(CancellationToken ct = default)
        where TEntity : class, IEntity<string>
    {
        var failedJobs = (await EmbedJob<TEntity>.Query(
            j => j.Status == EmbedJobStatus.Failed || j.Status == EmbedJobStatus.FailedPermanent,
            ct)).ToList();

        foreach (var job in failedJobs)
        {
            job.Status = EmbedJobStatus.Pending;
            job.RetryCount = 0;
            job.Error = null;
            job.StartedAt = null;
            job.CompletedAt = null;
            await job.Save(ct);
        }

        return failedJobs.Count;
    }

    /// <summary>
    /// Purges completed jobs older than the specified age.
    /// </summary>
    public static async Task<int> PurgeCompleted<TEntity>(
        TimeSpan olderThan,
        CancellationToken ct = default)
        where TEntity : class, IEntity<string>
    {
        var cutoff = DateTimeOffset.UtcNow - olderThan;
        var oldJobs = (await EmbedJob<TEntity>.Query(
            j => j.Status == EmbedJobStatus.Completed && j.CompletedAt < cutoff,
            ct)).ToList();

        foreach (var job in oldJobs)
        {
            await EmbedJob<TEntity>.Remove(job.Id!, ct);
        }

        return oldJobs.Count;
    }

    /// <summary>
    /// Purges ALL completed jobs regardless of age.
    /// </summary>
    public static async Task<int> PurgeAllCompleted<TEntity>(CancellationToken ct = default)
        where TEntity : class, IEntity<string>
    {
        var completedJobs = (await EmbedJob<TEntity>.Query(
            j => j.Status == EmbedJobStatus.Completed,
            ct)).ToList();

        foreach (var job in completedJobs)
        {
            await EmbedJob<TEntity>.Remove(job.Id!, ct);
        }

        return completedJobs.Count;
    }

    /// <summary>
    /// Cancels all pending jobs for a specific entity type.
    /// </summary>
    public static async Task<int> CancelPending<TEntity>(CancellationToken ct = default)
        where TEntity : class, IEntity<string>
    {
        var pendingJobs = (await EmbedJob<TEntity>.Query(
            j => j.Status == EmbedJobStatus.Pending,
            ct)).ToList();

        foreach (var job in pendingJobs)
        {
            await EmbedJob<TEntity>.Remove(job.Id!, ct);
        }

        return pendingJobs.Count;
    }

    /// <summary>
    /// Gets statistics about embedding jobs for a specific entity type.
    /// </summary>
    public static async Task<EmbedJobStats> GetStats<TEntity>(CancellationToken ct = default)
        where TEntity : class, IEntity<string>
    {
        var allJobs = (await EmbedJob<TEntity>.All(ct)).ToList();

        var stats = new EmbedJobStats
        {
            EntityType = typeof(TEntity).Name,
            TotalJobs = allJobs.Count,
            PendingCount = allJobs.Count(j => j.Status == EmbedJobStatus.Pending),
            ProcessingCount = allJobs.Count(j => j.Status == EmbedJobStatus.Processing),
            CompletedCount = allJobs.Count(j => j.Status == EmbedJobStatus.Completed),
            FailedCount = allJobs.Count(j => j.Status == EmbedJobStatus.Failed),
            FailedPermanentCount = allJobs.Count(j => j.Status == EmbedJobStatus.FailedPermanent)
        };

        // Calculate average processing time for completed jobs
        var completedJobs = allJobs
            .Where(j => j.Status == EmbedJobStatus.Completed && j.StartedAt.HasValue && j.CompletedAt.HasValue)
            .ToList();

        if (completedJobs.Any())
        {
            var avgDuration = completedJobs
                .Select(j => (j.CompletedAt!.Value - j.StartedAt!.Value).TotalSeconds)
                .Average();

            stats.AvgProcessingTimeSeconds = avgDuration;
        }

        // Find oldest pending job
        var oldestPending = allJobs
            .Where(j => j.Status == EmbedJobStatus.Pending)
            .OrderBy(j => j.CreatedAt)
            .FirstOrDefault();

        if (oldestPending != null)
        {
            stats.OldestPendingAge = DateTimeOffset.UtcNow - oldestPending.CreatedAt;
        }

        return stats;
    }

    /// <summary>
    /// Gets detailed information about failed jobs.
    /// </summary>
    public static async Task<List<FailedJobInfo>> GetFailedJobs<TEntity>(
        int limit = 100,
        CancellationToken ct = default)
        where TEntity : class, IEntity<string>
    {
        var failedJobs = (await EmbedJob<TEntity>.Query(
            j => j.Status == EmbedJobStatus.Failed || j.Status == EmbedJobStatus.FailedPermanent,
            ct))
            .OrderByDescending(j => j.CreatedAt)
            .Take(limit)
            .ToList();

        return failedJobs.Select(j => new FailedJobInfo
        {
            JobId = j.Id!,
            EntityId = j.EntityId,
            EntityType = j.EntityType,
            Status = j.Status.ToString(),
            Error = j.Error ?? "Unknown error",
            RetryCount = j.RetryCount,
            CreatedAt = j.CreatedAt,
            StartedAt = j.StartedAt,
            CompletedAt = j.CompletedAt
        }).ToList();
    }

    /// <summary>
    /// Requeues a specific job by entity ID.
    /// </summary>
    public static async Task<bool> RequeueJob<TEntity>(
        string entityId,
        CancellationToken ct = default)
        where TEntity : class, IEntity<string>
    {
        var jobId = EmbedJob<TEntity>.MakeId(entityId);
        var job = await EmbedJob<TEntity>.Get(jobId, ct);

        if (job == null)
            return false;

        job.Status = EmbedJobStatus.Pending;
        job.RetryCount = 0;
        job.Error = null;
        job.StartedAt = null;
        job.CompletedAt = null;

        await job.Save(ct);
        return true;
    }
}

/// <summary>
/// Statistics about embedding jobs for a specific entity type.
/// </summary>
public class EmbedJobStats
{
    public required string EntityType { get; set; }
    public int TotalJobs { get; set; }
    public int PendingCount { get; set; }
    public int ProcessingCount { get; set; }
    public int CompletedCount { get; set; }
    public int FailedCount { get; set; }
    public int FailedPermanentCount { get; set; }
    public double? AvgProcessingTimeSeconds { get; set; }
    public TimeSpan? OldestPendingAge { get; set; }
}

/// <summary>
/// Information about a failed job.
/// </summary>
public class FailedJobInfo
{
    public required string JobId { get; set; }
    public required string EntityId { get; set; }
    public required string EntityType { get; set; }
    public required string Status { get; set; }
    public required string Error { get; set; }
    public int RetryCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
