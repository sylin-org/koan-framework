using Koan.Data.Core;
using Koan.Rag.Abstractions;
using Koan.Rag.Infrastructure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Rag.Workers;

/// <summary>
/// Background service that processes queued RAG ingestion jobs.
/// Uses <see cref="RagJobProcessorRegistry"/> for typed entity loading —
/// no runtime reflection. Processors are pre-registered by the auto-registrar.
/// </summary>
internal sealed class RagIngestionWorker(
    ILogger<RagIngestionWorker> logger,
    IOptions<RagOptions> options,
    IConceptGraphStore graphStore,
    IDistillationTreeStore treeStore,
    RagJobProcessorRegistry processorRegistry) : BackgroundService
{
    private readonly RagOptions _config = options.Value;
    private static readonly TimeSpan ActivePollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan IdlePollInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromSeconds(5);
    private const double RetryBackoffMultiplier = 2.0;
    private const int BatchSize = 10;

    private readonly Queue<DateTimeOffset> _recentCalls = new();
    private const int MaxCallsPerMinute = 100;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "RagIngestionWorker started (GraphStrategy={Strategy})",
            _config.GraphStrategy);

        // Load persisted concept graph and distillation tree on startup
        await graphStore.Load(stoppingToken);
        await treeStore.Load(stoppingToken);

        // Recover stale Processing jobs (crashed before completion)
        await RecoverStaleJobs(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await ProcessBatch(stoppingToken);

                var delay = processed > 0 ? ActivePollInterval : IdlePollInterval;
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "RagIngestionWorker encountered error in main loop");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }

        // Persist graph and tree state on shutdown
        await graphStore.Save(CancellationToken.None);
        await treeStore.Save(CancellationToken.None);
        logger.LogInformation("RagIngestionWorker stopped");
    }

    private async Task<int> ProcessBatch(CancellationToken ct)
    {
        if (!processorRegistry.HasProcessors)
            return 0;

        var pendingJobs = await QueryPendingJobs(logger, ct);
        if (pendingJobs.Count == 0)
            return 0;

        var processedCount = 0;

        foreach (var job in pendingJobs)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                job.Status = RagIngestionStatus.Processing;
                job.StartedAt = DateTimeOffset.UtcNow;
                await job.Save(ct);

                // Throttle outbound calls to respect rate limits
                await WaitForRateLimit(ct);

                // Dispatch to the pre-registered typed processor
                await processorRegistry.Process(job, ct);

                try
                {
                    job.Status = RagIngestionStatus.Completed;
                    job.CompletedAt = DateTimeOffset.UtcNow;
                    await job.Save(ct);

                    processedCount++;
                    logger.LogDebug(
                        "Completed RAG ingestion job {JobId} for {EntityType}:{EntityId}",
                        job.Id, job.EntityType, job.EntityId);
                }
                catch (Exception saveEx) when (saveEx is not OperationCanceledException)
                {
                    logger.LogError(saveEx,
                        "RAG ingestion job {JobId} completed but failed to persist Completed state; resetting to Pending",
                        job.Id);

                    job.Status = RagIngestionStatus.Pending;
                    job.StartedAt = null;
                    job.CompletedAt = null;
                    await job.Save(CancellationToken.None);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex,
                    "RAG ingestion job {JobId} failed: {Error}",
                    job.Id, ex.Message);

                await HandleJobFailure(job, ex.Message, ct);
            }
        }

        if (processedCount > 0)
        {
            await graphStore.Save(ct);

            // Periodic cleanup of old completed jobs (keep last 24 hours)
            await CleanupCompletedJobs(ct);
        }

        return processedCount;
    }

    private async Task HandleJobFailure(RagIngestionJob job, string error, CancellationToken ct)
    {
        job.Error = error;
        job.RetryCount++;

        if (job.RetryCount >= job.MaxRetries)
        {
            job.Status = RagIngestionStatus.FailedPermanent;
            job.CompletedAt = DateTimeOffset.UtcNow;

            logger.LogError(
                "RAG ingestion job {JobId} permanently failed after {Retries} retries: {Error}",
                job.Id, job.RetryCount, error);
        }
        else
        {
            var delay = CalculateRetryDelay(job.RetryCount);
            logger.LogWarning(
                "RAG ingestion job {JobId} failed (retry {Retry}/{Max}), next attempt in {Delay}: {Error}",
                job.Id, job.RetryCount, job.MaxRetries, delay, error);

            job.Status = RagIngestionStatus.Pending;
            job.StartedAt = null;
        }

        await job.Save(ct);
    }

    private async Task RecoverStaleJobs(CancellationToken ct)
    {
        try
        {
            var staleJobs = await RagIngestionJob.Query(
                j => j.Status == RagIngestionStatus.Processing, ct);

            foreach (var job in staleJobs)
            {
                job.Status = RagIngestionStatus.Pending;
                job.StartedAt = null;
                await job.Save(ct);
            }

            if (staleJobs.Count > 0)
                logger.LogInformation("Recovered {Count} stale Processing jobs to Pending", staleJobs.Count);
        }
        catch { /* Job store may not be available yet */ }
    }

    private async Task WaitForRateLimit(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var windowStart = now.AddMinutes(-1);

        // Purge old entries
        while (_recentCalls.Count > 0 && _recentCalls.Peek() < windowStart)
            _recentCalls.Dequeue();

        if (_recentCalls.Count >= MaxCallsPerMinute)
        {
            var oldestInWindow = _recentCalls.Peek();
            var waitTime = oldestInWindow.AddMinutes(1) - now;
            if (waitTime > TimeSpan.Zero)
                await Task.Delay(waitTime, ct);
        }

        _recentCalls.Enqueue(now);
    }

    private static async Task CleanupCompletedJobs(CancellationToken ct)
    {
        try
        {
            var cutoff = DateTimeOffset.UtcNow.AddHours(-24);
            var oldJobs = await RagIngestionJob.Query(
                j => j.Status == RagIngestionStatus.Completed && j.CompletedAt < cutoff, ct);

            foreach (var job in oldJobs.Take(50)) // Batch limit
                await RagIngestionJob.Remove(job.Id, ct);
        }
        catch { /* Cleanup is best-effort */ }
    }

    private static async Task<List<RagIngestionJob>> QueryPendingJobs(
        ILogger<RagIngestionWorker> logger, CancellationToken ct)
    {
        try
        {
            var jobs = await RagIngestionJob.Query(
                j => j.Status == RagIngestionStatus.Pending, ct);

            return jobs
                .OrderByDescending(j => j.Priority)
                .ThenBy(j => j.CreatedAt)
                .Take(BatchSize)
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to query pending RAG ingestion jobs");
            return [];
        }
    }

    private static TimeSpan CalculateRetryDelay(int retryCount)
    {
        var seconds = InitialRetryDelay.TotalSeconds *
                      Math.Pow(RetryBackoffMultiplier, retryCount - 1);
        return TimeSpan.FromSeconds(Math.Min(seconds, 300));
    }
}
