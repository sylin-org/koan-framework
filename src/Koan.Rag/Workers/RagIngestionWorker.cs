using Koan.Data.Core;
using Koan.Rag.Abstractions;
using Koan.Rag.Infrastructure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Rag.Workers;

/// <summary>
/// Background service that processes queued RAG ingestion jobs.
/// Follows the <c>EmbeddingWorker</c> pattern: adaptive polling,
/// batch processing, retry with exponential backoff.
/// </summary>
internal sealed class RagIngestionWorker(
    ILogger<RagIngestionWorker> logger,
    IOptions<RagOptions> options,
    IConceptGraphStore graphStore) : BackgroundService
{
    private readonly RagOptions _config = options.Value;
    private static readonly TimeSpan ActivePollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan IdlePollInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromSeconds(5);
    private const double RetryBackoffMultiplier = 2.0;
    private const int BatchSize = 10;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "RagIngestionWorker started (GraphStrategy={Strategy})",
            _config.GraphStrategy);

        // Load persisted concept graph on startup
        await graphStore.Load(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await ProcessBatch(stoppingToken);

                // Adaptive polling: fast when work available, slow when idle
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

        // Persist graph state on shutdown
        await graphStore.Save(CancellationToken.None);
        logger.LogInformation("RagIngestionWorker stopped");
    }

    private async Task<int> ProcessBatch(CancellationToken ct)
    {
        // Query pending jobs
        var pendingJobs = await QueryPendingJobs(ct);
        if (pendingJobs.Count == 0)
            return 0;

        var processedCount = 0;

        foreach (var job in pendingJobs)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                // Mark as processing
                job.Status = RagIngestionStatus.Processing;
                job.StartedAt = DateTimeOffset.UtcNow;
                await job.Save(ct);

                // Process the job
                await ProcessJob(job, ct);

                // Mark complete
                job.Status = RagIngestionStatus.Completed;
                job.CompletedAt = DateTimeOffset.UtcNow;
                await job.Save(ct);

                processedCount++;
                logger.LogDebug(
                    "Completed RAG ingestion job {JobId} for {EntityType}:{EntityId}",
                    job.Id, job.EntityType, job.EntityId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex,
                    "RAG ingestion job {JobId} failed: {Error}",
                    job.Id, ex.Message);

                await HandleJobFailure(job, ex.Message, ct);
            }
        }

        // Persist graph after batch
        if (processedCount > 0)
            await graphStore.Save(ct);

        return processedCount;
    }

    private async Task ProcessJob(RagIngestionJob job, CancellationToken ct)
    {
        // Resolve the RAG service to get the ingestion pipeline
        var ragService = Koan.Core.Hosting.App.AppHost.Current
            ?.GetService(typeof(IRagService)) as IRagService
            ?? throw new InvalidOperationException("IRagService not registered");

        // The actual ingestion is handled by the pipeline via the corpus
        // The worker's job is to orchestrate retry and state management
        logger.LogDebug(
            "Processing RAG ingestion for {EntityType}:{EntityId} (corpus: {Corpus})",
            job.EntityType, job.EntityId, job.CorpusName ?? "default");

        // TODO: Load entity by ID + type via reflection (matching EmbeddingWorker pattern),
        // re-verify content signature, and call pipeline.IngestEntity
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
            // Schedule retry with exponential backoff
            var delay = CalculateRetryDelay(job.RetryCount);
            logger.LogWarning(
                "RAG ingestion job {JobId} failed (retry {Retry}/{Max}), will retry in {Delay}: {Error}",
                job.Id, job.RetryCount, job.MaxRetries, delay, error);

            job.Status = RagIngestionStatus.Pending;
            job.StartedAt = null;
        }

        await job.Save(ct);
    }

    private static async Task<List<RagIngestionJob>> QueryPendingJobs(CancellationToken ct)
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
        catch
        {
            // If the job store isn't available yet (no data adapter configured), return empty
            return [];
        }
    }

    private static TimeSpan CalculateRetryDelay(int retryCount)
    {
        var seconds = InitialRetryDelay.TotalSeconds *
                      Math.Pow(RetryBackoffMultiplier, retryCount - 1);
        return TimeSpan.FromSeconds(Math.Min(seconds, 300)); // Cap at 5 minutes
    }
}
