using Koan.AI;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Vector;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace Koan.Data.AI.Workers;

/// <summary>
/// Background service that processes async embedding jobs from the queue.
/// Part of ARCH-0070: Attribute-Driven AI Embeddings (Phase 3).
/// </summary>
public class EmbeddingWorker : BackgroundService
{
    private readonly ILogger<EmbeddingWorker> _logger;
    private readonly IOptions<EmbeddingWorkerOptions> _options;

    // Rate limiting: track embeddings generated per minute
    private readonly ConcurrentQueue<DateTimeOffset> _recentEmbeddings = new();
    private readonly SemaphoreSlim _rateLimitSemaphore = new(1, 1);

    public EmbeddingWorker(
        ILogger<EmbeddingWorker> logger,
        IOptions<EmbeddingWorkerOptions> options)
    {
        _logger = logger;
        _options = options;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Value.Enabled)
        {
            _logger.LogInformation("EmbeddingWorker disabled via configuration");
            return;
        }

        _logger.LogInformation("EmbeddingWorker started (BatchSize={BatchSize}, RateLimit={RateLimit}/min)",
            _options.Value.BatchSize, _options.Value.GlobalRateLimitPerMinute);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processedCount = await ProcessBatchAsync(stoppingToken);

                if (processedCount > 0)
                {
                    // Jobs available - poll frequently
                    await Task.Delay(_options.Value.PollInterval, stoppingToken);
                }
                else
                {
                    // No jobs - poll less frequently
                    await Task.Delay(_options.Value.IdlePollInterval, stoppingToken);
                }

                // Periodic cleanup of completed jobs
                if (_options.Value.AutoCleanupCompleted)
                {
                    await CleanupCompletedJobsAsync(stoppingToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "EmbeddingWorker encountered error in main loop");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }

        _logger.LogInformation("EmbeddingWorker stopped");
    }

    /// <summary>
    /// Processes a batch of pending jobs across all entity types.
    /// </summary>
    private async Task<int> ProcessBatchAsync(CancellationToken ct)
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
                var count = await ProcessEntityTypeJobsAsync(entityType, ct);
                processedCount += count;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process jobs for entity type {EntityType}", entityType.Name);
            }
        }

        return processedCount;
    }

    /// <summary>
    /// Processes pending jobs for a specific entity type using reflection.
    /// </summary>
    private async Task<int> ProcessEntityTypeJobsAsync(Type entityType, CancellationToken ct)
    {
        // Use reflection to call ProcessJobsAsync<TEntity>
        var method = typeof(EmbeddingWorker)
            .GetMethod(nameof(ProcessJobsAsync), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.MakeGenericMethod(entityType);

        if (method == null)
        {
            _logger.LogError("Failed to find ProcessJobsAsync method for {EntityType}", entityType.Name);
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
        // Query for pending jobs, ordered by priority (high first) then creation time (FIFO)
        var pendingJobs = (await EmbedJob<TEntity>.Query(
            j => j.Status == EmbedJobStatus.Pending,
            ct))
            .OrderByDescending(j => j.Priority)
            .ThenBy(j => j.CreatedAt)
            .Take(_options.Value.BatchSize)
            .ToList();

        if (!pendingJobs.Any())
            return 0;

        _logger.LogDebug("Processing {Count} pending jobs for {EntityType}",
            pendingJobs.Count, typeof(TEntity).Name);

        var processedCount = 0;
        foreach (var job in pendingJobs)
        {
            try
            {
                // Check rate limit before processing
                await WaitForRateLimitAsync(ct);

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
                _logger.LogDebug("Completed embedding job {JobId} for entity {EntityId}",
                    job.Id, job.EntityId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process embedding job {JobId}: {Error}",
                    job.Id, ex.Message);

                await HandleJobFailureAsync(job, ex.Message, ct);
            }
        }

        return processedCount;
    }

    /// <summary>
    /// Processes a single embedding job.
    /// </summary>
    private async Task ProcessJobAsync<TEntity>(EmbedJob<TEntity> job, CancellationToken ct)
        where TEntity : class, IEntity<string>
    {
        // Load the entity to get fresh data
        var entity = await Data<TEntity, string>.GetAsync(job.EntityId, ct);
        if (entity == null)
        {
            throw new InvalidOperationException($"Entity {job.EntityId} not found");
        }

        // Verify content signature hasn't changed
        var metadata = EmbeddingMetadata.Get<TEntity>();
        var currentSignature = metadata.ComputeSignature(entity);

        if (currentSignature != job.ContentSignature)
        {
            _logger.LogWarning(
                "Content signature changed for entity {EntityId} - regenerating embedding text",
                job.EntityId);

            // Update job with new content
            job.EmbeddingText = metadata.BuildEmbeddingText(entity);
            job.ContentSignature = currentSignature;
        }

        // Generate embedding
        var embedding = await Ai.Embed(job.EmbeddingText, ct);

        // Store in vector database
        await VectorData<TEntity>.SaveWithVector(entity, embedding, null, ct);

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

        if (job.RetryCount >= _options.Value.MaxRetries)
        {
            // Permanently failed
            job.Status = EmbedJobStatus.FailedPermanent;
            job.CompletedAt = DateTimeOffset.UtcNow;

            _logger.LogError(
                "Embedding job {JobId} permanently failed after {Retries} retries: {Error}",
                job.Id, job.RetryCount, error);
        }
        else
        {
            // Schedule retry with exponential backoff
            job.Status = EmbedJobStatus.Failed;

            var delay = CalculateRetryDelay(job.RetryCount);
            _logger.LogWarning(
                "Embedding job {JobId} failed (retry {Retry}/{MaxRetries}), will retry in {Delay}: {Error}",
                job.Id, job.RetryCount, _options.Value.MaxRetries, delay, error);

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
        var delay = _options.Value.InitialRetryDelay.TotalSeconds *
                    Math.Pow(_options.Value.RetryBackoffMultiplier, retryCount - 1);

        var clampedDelay = Math.Min(delay, _options.Value.MaxRetryDelay.TotalSeconds);
        return TimeSpan.FromSeconds(clampedDelay);
    }

    /// <summary>
    /// Waits if rate limit is exceeded.
    /// </summary>
    private async Task WaitForRateLimitAsync(CancellationToken ct)
    {
        if (_options.Value.GlobalRateLimitPerMinute <= 0)
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
            while (_recentEmbeddings.Count >= _options.Value.GlobalRateLimitPerMinute)
            {
                _logger.LogDebug("Rate limit reached, waiting...");
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
    private async Task CleanupCompletedJobsAsync(CancellationToken ct)
    {
        try
        {
            var cutoff = DateTimeOffset.UtcNow - _options.Value.CompletedJobRetention;

            foreach (var entityType in EmbeddingRegistry.GetRegisteredTypes())
            {
                if (!EmbeddingRegistry.AsyncEntityTypes.Contains(entityType))
                    continue;

                await CleanupEntityTypeJobsAsync(entityType, cutoff, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cleanup completed jobs");
        }
    }

    /// <summary>
    /// Cleans up completed jobs for a specific entity type.
    /// </summary>
    private async Task CleanupEntityTypeJobsAsync(Type entityType, DateTimeOffset cutoff, CancellationToken ct)
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

            _logger.LogInformation(
                "Cleaned up {Count} completed jobs for {EntityType}",
                oldJobs.Count, typeof(TEntity).Name);
        }
    }
}
