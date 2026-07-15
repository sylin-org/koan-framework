using Koan.AI;
using Koan.AI.Contracts.Options;
using Koan.Core.Context;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Vector;
using Koan.Data.AI.Telemetry;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Koan.Data.AI.Workers;

/// <summary>
/// Background service that processes async embedding jobs from the queue.
/// Part of ARCH-0070: Attribute-Driven AI Embeddings (Phase 3).
/// </summary>
public class EmbeddingWorker(
    ILogger<EmbeddingWorker> logger,
    IOptions<EmbeddingWorkerOptions> options,
    EmbeddingTelemetry? telemetry,
    KoanContextCarrierRegistry contextCarriers) : BackgroundService
{
    /// <summary>Compatibility constructor for the public 0.17.0 infrastructure shape.</summary>
    [Obsolete("Direct EmbeddingWorker construction is compatibility-only; let AddKoan compose Core context.")]
    public EmbeddingWorker(
        ILogger<EmbeddingWorker> logger,
        IOptions<EmbeddingWorkerOptions> options,
        EmbeddingTelemetry? telemetry = null)
        : this(logger, options, telemetry, new KoanContextCarrierRegistry([]))
    {
    }

    // Rate limiting: track embeddings generated per minute
    private readonly ConcurrentQueue<DateTimeOffset> _recentEmbeddings = new();
    private readonly SemaphoreSlim _rateLimitSemaphore = new(1, 1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("EmbeddingWorker disabled via configuration");
            return;
        }

        logger.LogInformation("EmbeddingWorker started (BatchSize={BatchSize}, RateLimit={RateLimit}/min)",
            options.Value.BatchSize, options.Value.GlobalRateLimitPerMinute);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processedCount = await ProcessBatch(stoppingToken);

                if (processedCount > 0)
                {
                    // Jobs available - poll frequently
                    await Task.Delay(options.Value.PollInterval, stoppingToken);
                }
                else
                {
                    // No jobs - poll less frequently
                    await Task.Delay(options.Value.IdlePollInterval, stoppingToken);
                }

                // Periodic cleanup of completed jobs
                if (options.Value.AutoCleanupCompleted)
                {
                    await CleanupCompletedJobs(stoppingToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "EmbeddingWorker encountered error in main loop");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }

        logger.LogInformation("EmbeddingWorker stopped");
    }

    /// <summary>
    /// Processes a batch of pending jobs across all entity types.
    /// </summary>
    private async Task<int> ProcessBatch(CancellationToken ct)
    {
        var processedCount = 0;

        // Process jobs for each registered entity type
        foreach (var entityType in EmbeddingRegistry.GetRegisteredTypes())
        {
            // Only process entity types with Async=true
            if (!EmbeddingRegistry.AsyncEntityTypes.Contains(entityType))
                continue;

            try
            {
                var count = await ProcessEntityTypeJobs(entityType, ct);
                processedCount += count;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to process jobs for entity type {EntityType}", entityType.Name);
            }
        }

        return processedCount;
    }

    /// <summary>
    /// Processes pending jobs for a specific entity type using reflection.
    /// </summary>
    private async Task<int> ProcessEntityTypeJobs(Type entityType, CancellationToken ct)
    {
        // Use reflection to call ProcessJobsAsync<TEntity>
        var method = typeof(EmbeddingWorker)
            .GetMethod(nameof(ProcessJobsAsync), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.MakeGenericMethod(entityType);

        if (method == null)
        {
            logger.LogError("Failed to find ProcessJobsAsync method for {EntityType}", entityType.Name);
            return 0;
        }

        var task = method.Invoke(this, new object[] { ct }) as Task<int>;
        return task != null ? await task : 0;
    }

    /// <summary>
    /// Processes pending jobs for a specific entity type.
    /// </summary>
    private async Task<int> ProcessJobsAsync<TEntity>(CancellationToken ct)
        where TEntity : class, IEntity<string>
    {
        var batchStopwatch = Stopwatch.StartNew();

        // Query for pending jobs, ordered by priority (high first) then creation time (FIFO)
        var pendingJobs = (await EmbedJob<TEntity>.Query(
            j => j.Status == EmbedJobStatus.Pending,
            ct))
            .OrderByDescending(j => j.Priority)
            .ThenBy(j => j.CreatedAt)
            .Take(options.Value.BatchSize)
            .ToList();

        if (!pendingJobs.Any())
            return 0;

        logger.LogDebug("Processing {Count} pending jobs for {EntityType}",
            pendingJobs.Count, typeof(TEntity).Name);

        var processedCount = 0;
        foreach (var job in pendingJobs)
        {
            try
            {
                // Check rate limit before processing
                await WaitForRateLimit(ct);

                // Mark job as processing
                job.Status = EmbedJobStatus.Processing;
                job.StartedAt = DateTimeOffset.UtcNow;
                await job.Save(ct);

                // Process the job
                await ProcessJobAsync(job, ct);

                // Mark job as completed
                job.Status = EmbedJobStatus.Completed;
                job.CompletedAt = DateTimeOffset.UtcNow;
                await job.Save(ct);

                processedCount++;
                logger.LogDebug("Completed embedding job {JobId} for entity {EntityId}",
                    job.Id, job.EntityId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to process embedding job {JobId}: {Error}",
                    job.Id, ex.Message);

                await HandleJobFailureAsync(job, ex.Message, ct);
            }
        }

        batchStopwatch.Stop();

        // Record batch processing metrics
        telemetry?.RecordBatchProcessing(
            entityType: typeof(TEntity).Name,
            batchSize: processedCount,
            durationSeconds: batchStopwatch.Elapsed.TotalSeconds);

        // Record queue processing metrics
        telemetry?.RecordQueueProcessing(
            count: processedCount,
            success: true,
            entityType: typeof(TEntity).Name);

        // Update queue state (get latest counts for telemetry)
        var allPending = await EmbedJob<TEntity>.Query(j => j.Status == EmbedJobStatus.Pending, ct);
        var allFailed = await EmbedJob<TEntity>.Query(j => j.Status == EmbedJobStatus.FailedPermanent, ct);
        var oldestPending = allPending.OrderBy(j => j.CreatedAt).FirstOrDefault();
        var oldestAge = oldestPending != null ? (DateTimeOffset.UtcNow - oldestPending.CreatedAt).TotalSeconds : 0.0;

        telemetry?.UpdateQueueState(
            pending: allPending.Count(),
            failed: allFailed.Count(),
            oldestAgeSeconds: oldestAge);

        return processedCount;
    }

    /// <summary>
    /// Processes a single embedding job.
    /// </summary>
    private async Task ProcessJobAsync<TEntity>(EmbedJob<TEntity> job, CancellationToken ct)
        where TEntity : class, IEntity<string>
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Restore the Koan context (tenant + access subject) captured at enqueue so this global worker
            // reads/writes the scoped entity, vector, and state in the scope it belongs to. Without it, a
            // [AccessScoped]/tenant-scoped entity reads back null (fail-closed) → "not found". Fail-closed itself: an
            // unrestorable carrier throws here and the job is retried/dead-lettered, never silently mis-scoped.
            using var _ambient = contextCarriers.Restore(job.AmbientCarrier, ContextIngressTrust.HostTrusted);

            // Load the entity to get fresh data
            var entity = await Data<TEntity, string>.Get(job.EntityId, ct);
            if (entity == null)
            {
                throw new InvalidOperationException($"Entity {job.EntityId} not found");
            }

            // Verify content signature hasn't changed
            var metadata = EmbeddingMetadata.Resolve<TEntity>();
            var currentSignature = metadata.ComputeSignature(entity);

            if (currentSignature != job.ContentSignature)
            {
                logger.LogWarning(
                    "Content signature changed for entity {EntityId} - regenerating embedding text",
                    job.EntityId);

                // Update job with new content
                job.EmbeddingText = metadata.BuildEmbeddingText(entity);
                job.ContentSignature = currentSignature;

                // Record cache invalidation
                telemetry?.RecordCacheInvalidation(typeof(TEntity).Name, "content_changed");
            }

            // Estimate tokens for cost tracking
            var estimatedTokens = EmbeddingMetadata.EstimateTokens(job.EmbeddingText);

            // Generate embedding with source routing AND the configured embed model. Passing the model is
            // essential: without it the embed falls through to the adapter's DefaultModel, which for a mixed
            // vision+embedding deployment may be a chat/vision model that cannot embed (HTTP 500 "does not support
            // embeddings"). The model is [Embedding(Model=…)] (metadata.Model) or the job's captured model.
            var embedModel = metadata.Model ?? job.Model;
            float[] embedding;
            using (metadata.Source != null ? Client.Scope(all: metadata.Source) : null)
            {
                embedding = string.IsNullOrWhiteSpace(embedModel)
                    ? await Client.Embed(job.EmbeddingText, ct)
                    : await Client.Embed(job.EmbeddingText, new EmbedOptions { Model = embedModel }, ct);
            }

            stopwatch.Stop();

            // Estimate cost
            var estimatedCost = EmbeddingCostEstimator.EstimateCost(
                metadata.Model ?? job.Model,
                metadata.Source?.Split('-').FirstOrDefault(), // Extract provider from source like "openai-prod"
                estimatedTokens);

            // Record telemetry
            telemetry?.RecordEmbeddingGeneration(
                entityType: typeof(TEntity).Name,
                model: metadata.Model ?? job.Model,
                provider: metadata.Source?.Split('-').FirstOrDefault(),
                source: metadata.Source,
                latencyMs: stopwatch.Elapsed.TotalMilliseconds,
                tokens: estimatedTokens,
                estimatedCost: estimatedCost,
                success: true);

            // W4 (AI-0036 P2): fail loud before writing if this model would make the index mixed-space.
            await VectorModelGuard.GuardWrite<TEntity>(metadata.Model ?? job.Model, ct: ct);

            // Store in vector database — stamp the producing model/source (provenance, AI-0036 W1) plus
            // the entity's filterable facets under their CLR property names (AI-0036 D1), so metadata
            // filters (incl. the Chain lambda DX) work by construction.
            var provenance = VectorProvenance.Build(
                metadata.Model ?? job.Model, metadata.Source, metadata.Version,
                merge: VectorFilterableMetadata.Extract(entity));
            await VectorData<TEntity>.SaveWithVector(entity, embedding, provenance, ct);

            // Update embedding state
            var stateId = EmbeddingState<TEntity>.MakeId(job.EntityId);
            var state = await EmbeddingState<TEntity>.Get(stateId, ct);

            if (state == null)
            {
                state = new EmbeddingState<TEntity>
                {
                    Id = stateId,
                    EntityId = job.EntityId,
                    ContentSignature = currentSignature,
                    LastEmbeddedAt = DateTimeOffset.UtcNow,
                    Model = job.Model
                };
            }
            else
            {
                state.ContentSignature = currentSignature;
                state.LastEmbeddedAt = DateTimeOffset.UtcNow;
                state.Model = job.Model;
            }

            await state.Save(ct);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            // Record failure telemetry
            var metadata = EmbeddingMetadata.Resolve<TEntity>();
            var estimatedTokens = EmbeddingMetadata.EstimateTokens(job.EmbeddingText);

            telemetry?.RecordEmbeddingGeneration(
                entityType: typeof(TEntity).Name,
                model: metadata.Model ?? job.Model,
                provider: metadata.Source?.Split('-').FirstOrDefault(),
                source: metadata.Source,
                latencyMs: stopwatch.Elapsed.TotalMilliseconds,
                tokens: estimatedTokens,
                estimatedCost: 0.0,
                success: false,
                errorMessage: ex.Message);

            throw; // Re-throw for caller's error handling
        }
    }

    /// <summary>
    /// Handles job failure with retry logic.
    /// </summary>
    private async Task HandleJobFailureAsync<TEntity>(
        EmbedJob<TEntity> job,
        string error,
        CancellationToken ct)
        where TEntity : class, IEntity<string>
    {
        job.Error = error;
        job.RetryCount++;

        if (job.RetryCount >= options.Value.MaxRetries)
        {
            // Permanently failed
            job.Status = EmbedJobStatus.FailedPermanent;
            job.CompletedAt = DateTimeOffset.UtcNow;

            logger.LogError(
                "Embedding job {JobId} permanently failed after {Retries} retries: {Error}",
                job.Id, job.RetryCount, error);
        }
        else
        {
            // Schedule retry with exponential backoff
            job.Status = EmbedJobStatus.Failed;

            var delay = CalculateRetryDelay(job.RetryCount);
            logger.LogWarning(
                "Embedding job {JobId} failed (retry {Retry}/{MaxRetries}), will retry in {Delay}: {Error}",
                job.Id, job.RetryCount, options.Value.MaxRetries, delay, error);

            // Reset to pending after delay (simplified - in production might use scheduled jobs)
            await Task.Delay(delay, ct);
            job.Status = EmbedJobStatus.Pending;
            job.StartedAt = null;
        }

        await job.Save(ct);
    }

    /// <summary>
    /// Calculates retry delay with exponential backoff.
    /// </summary>
    private TimeSpan CalculateRetryDelay(int retryCount)
    {
        var delay = options.Value.InitialRetryDelay.TotalSeconds *
                    Math.Pow(options.Value.RetryBackoffMultiplier, retryCount - 1);

        var clampedDelay = Math.Min(delay, options.Value.MaxRetryDelay.TotalSeconds);
        return TimeSpan.FromSeconds(clampedDelay);
    }

    /// <summary>
    /// Waits if rate limit is exceeded.
    /// </summary>
    private async Task WaitForRateLimit(CancellationToken ct)
    {
        if (options.Value.GlobalRateLimitPerMinute <= 0)
            return; // Rate limiting disabled

        await _rateLimitSemaphore.WaitAsync(ct);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var oneMinuteAgo = now.AddMinutes(-1);

            // Remove embeddings older than 1 minute
            while (_recentEmbeddings.TryPeek(out var timestamp) && timestamp < oneMinuteAgo)
            {
                _recentEmbeddings.TryDequeue(out _);
            }

            // Check if we're at the rate limit
            while (_recentEmbeddings.Count >= options.Value.GlobalRateLimitPerMinute)
            {
                logger.LogDebug("Rate limit reached, waiting...");
                await Task.Delay(TimeSpan.FromSeconds(1), ct);

                // Clean up old timestamps
                while (_recentEmbeddings.TryPeek(out var timestamp) && timestamp < now.AddMinutes(-1))
                {
                    _recentEmbeddings.TryDequeue(out _);
                }

                now = DateTimeOffset.UtcNow;
            }

            // Record this embedding
            _recentEmbeddings.Enqueue(now);
        }
        finally
        {
            _rateLimitSemaphore.Release();
        }
    }

    /// <summary>
    /// Cleans up old completed jobs.
    /// </summary>
    private async Task CleanupCompletedJobs(CancellationToken ct)
    {
        try
        {
            var cutoff = DateTimeOffset.UtcNow - options.Value.CompletedJobRetention;

            foreach (var entityType in EmbeddingRegistry.GetRegisteredTypes())
            {
                if (!EmbeddingRegistry.AsyncEntityTypes.Contains(entityType))
                    continue;

                await CleanupEntityTypeJobs(entityType, cutoff, ct);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to cleanup completed jobs");
        }
    }

    /// <summary>
    /// Cleans up completed jobs for a specific entity type.
    /// </summary>
    private async Task CleanupEntityTypeJobs(Type entityType, DateTimeOffset cutoff, CancellationToken ct)
    {
        var method = typeof(EmbeddingWorker)
            .GetMethod(nameof(CleanupJobsAsync), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.MakeGenericMethod(entityType);

        if (method == null)
            return;

        var task = method.Invoke(this, new object[] { cutoff, ct }) as Task;
        if (task != null)
            await task;
    }

    /// <summary>
    /// Cleans up completed jobs for a specific entity type.
    /// </summary>
    private async Task CleanupJobsAsync<TEntity>(DateTimeOffset cutoff, CancellationToken ct)
        where TEntity : class, IEntity<string>
    {
        var oldJobs = (await EmbedJob<TEntity>.Query(
            j => j.Status == EmbedJobStatus.Completed && j.CompletedAt < cutoff,
            ct)).ToList();

        if (oldJobs.Any())
        {
            foreach (var job in oldJobs)
            {
                await EmbedJob<TEntity>.Remove(job.Id!, ct);
            }

            logger.LogInformation(
                "Cleaned up {Count} completed jobs for {EntityType}",
                oldJobs.Count, typeof(TEntity).Name);
        }
    }
}
