using Koan.Data.Abstractions;
using Koan.Data.AI.Attributes;
using Koan.Data.AI.Options;
using Koan.Data.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace Koan.Data.AI.Workers;

/// <summary>
/// Background service that processes async media analysis jobs from the queue.
/// Mirrors <see cref="EmbeddingWorker"/> for [MediaAnalysis] entities with Async=true.
/// Picks up <see cref="MediaAnalysisState{TEntity}"/> records in Queued/Pending status,
/// runs analysis via <see cref="MediaAnalysisExecutor"/>, and updates state.
/// </summary>
public sealed class MediaAnalysisWorker(
    ILogger<MediaAnalysisWorker> logger,
    IOptions<MediaAnalysisOptions> options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("MediaAnalysisWorker disabled via configuration");
            return;
        }

        logger.LogInformation("MediaAnalysisWorker started (BatchSize={BatchSize})",
            options.Value.BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processedCount = await ProcessBatch(stoppingToken);

                var delay = processedCount > 0
                    ? options.Value.PollInterval
                    : options.Value.IdlePollInterval;

                await Task.Delay(delay, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "MediaAnalysisWorker encountered error in main loop");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }

        logger.LogInformation("MediaAnalysisWorker stopped");
    }

    /// <summary>
    /// Processes a batch of pending/queued analysis jobs across all registered entity types.
    /// </summary>
    private async Task<int> ProcessBatch(CancellationToken ct)
    {
        var processedCount = 0;

        foreach (var entityType in MediaAnalysisRegistry.AsyncEntityTypes)
        {
            try
            {
                var count = await ProcessEntityTypeJobs(entityType, ct);
                processedCount += count;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to process media analysis jobs for entity type {EntityType}",
                    entityType.Name);
            }
        }

        return processedCount;
    }

    /// <summary>
    /// Processes pending jobs for a specific entity type using reflection.
    /// </summary>
    private async Task<int> ProcessEntityTypeJobs(Type entityType, CancellationToken ct)
    {
        var method = typeof(MediaAnalysisWorker)
            .GetMethod(nameof(ProcessJobsAsync), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.MakeGenericMethod(entityType);

        if (method is null)
        {
            logger.LogError("Failed to find ProcessJobsAsync method for {EntityType}", entityType.Name);
            return 0;
        }

        var task = method.Invoke(this, [ct]) as Task<int>;
        return task is not null ? await task : 0;
    }

    /// <summary>
    /// Processes pending media analysis jobs for a specific entity type.
    /// </summary>
    private async Task<int> ProcessJobsAsync<TEntity>(CancellationToken ct)
        where TEntity : class, IEntity<string>
    {
        var sw = Stopwatch.StartNew();

        // Query for queued/pending states
        var pendingStates = (await MediaAnalysisState<TEntity>.Query(
            s => s.Status == MediaAnalysisStatus.Queued || s.Status == MediaAnalysisStatus.Pending,
            ct))
            .OrderBy(s => s.LastAttemptAt ?? DateTimeOffset.MinValue)
            .Take(options.Value.BatchSize)
            .ToList();

        if (pendingStates.Count == 0)
            return 0;

        logger.LogDebug("Processing {Count} pending media analysis jobs for {EntityType}",
            pendingStates.Count, typeof(TEntity).Name);

        var processedCount = 0;

        foreach (var state in pendingStates)
        {
            try
            {
                // Mark as processing
                var processingState = new MediaAnalysisState<TEntity>
                {
                    Id = state.Id!,
                    EntityId = state.EntityId,
                    Status = MediaAnalysisStatus.Processing,
                    AnalyzedVersion = state.AnalyzedVersion,
                    AttemptCount = state.AttemptCount + 1,
                    LastAttemptAt = DateTimeOffset.UtcNow,
                    ModeStatuses = state.ModeStatuses,
                };
                await processingState.Save(ct);

                // Load the entity
                var entity = await Data<TEntity, string>.Get(state.EntityId, ct);
                if (entity is null)
                {
                    logger.LogWarning("Entity {EntityId} not found for media analysis, marking failed",
                        state.EntityId);
                    var failedState = new MediaAnalysisState<TEntity>
                    {
                        Id = state.Id!,
                        EntityId = state.EntityId,
                        Status = MediaAnalysisStatus.Failed,
                        AnalyzedVersion = state.AnalyzedVersion,
                        AttemptCount = processingState.AttemptCount,
                        LastAttemptAt = processingState.LastAttemptAt,
                        FailureReason = "Entity not found",
                        ModeStatuses = state.ModeStatuses,
                    };
                    await failedState.Save(ct);
                    continue;
                }

                var metadata = MediaAnalysisMetadata.Resolve<TEntity>();
                if (metadata is null)
                {
                    logger.LogWarning("No metadata for {EntityType}, skipping", typeof(TEntity).Name);
                    continue;
                }

                // Extract bytes
                var bytes = EntityAi.ExtractBytes(entity);
                if (bytes is null || bytes.Length == 0)
                {
                    logger.LogWarning("No binary content on entity {EntityId} for media analysis",
                        state.EntityId);
                    var failedState = new MediaAnalysisState<TEntity>
                    {
                        Id = state.Id!,
                        EntityId = state.EntityId,
                        Status = MediaAnalysisStatus.Failed,
                        AnalyzedVersion = state.AnalyzedVersion,
                        AttemptCount = processingState.AttemptCount,
                        LastAttemptAt = processingState.LastAttemptAt,
                        FailureReason = "No binary content found",
                        ModeStatuses = state.ModeStatuses,
                    };
                    await failedState.Save(ct);
                    continue;
                }

                // Execute analysis
                var modeResults = await MediaAnalysisExecutor.Execute(entity, metadata, bytes, ct);

                // Save entity with analysis results
                await entity.Save(ct);

                // Determine overall status
                var allCompleted = modeResults.Values.All(m => m.Completed);
                var anyCompleted = modeResults.Values.Any(m => m.Completed);
                var overallStatus = allCompleted
                    ? MediaAnalysisStatus.Completed
                    : anyCompleted
                        ? MediaAnalysisStatus.PartiallyCompleted
                        : MediaAnalysisStatus.Failed;

                var completedState = new MediaAnalysisState<TEntity>
                {
                    Id = state.Id!,
                    EntityId = state.EntityId,
                    Status = overallStatus,
                    AnalyzedVersion = metadata.Version,
                    AttemptCount = processingState.AttemptCount,
                    LastAttemptAt = processingState.LastAttemptAt,
                    CompletedAt = DateTimeOffset.UtcNow,
                    ModeStatuses = modeResults,
                };
                await completedState.Save(ct);

                processedCount++;
                logger.LogDebug("Completed media analysis for entity {EntityId} ({Status})",
                    state.EntityId, overallStatus);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Failed to process media analysis for entity {EntityId}",
                    state.EntityId);
                await HandleJobFailure(state, ex.Message, ct);
            }
        }

        sw.Stop();
        if (processedCount > 0)
        {
            logger.LogInformation("Processed {Count} media analysis jobs for {EntityType} in {Elapsed}ms",
                processedCount, typeof(TEntity).Name, sw.ElapsedMilliseconds);
        }

        return processedCount;
    }

    /// <summary>
    /// Handles job failure with retry logic.
    /// </summary>
    private async Task HandleJobFailure<TEntity>(
        MediaAnalysisState<TEntity> state,
        string error,
        CancellationToken ct)
        where TEntity : class
    {
        var attemptCount = state.AttemptCount + 1;

        if (attemptCount >= options.Value.MaxRetries)
        {
            var failedState = new MediaAnalysisState<TEntity>
            {
                Id = state.Id!,
                EntityId = state.EntityId,
                Status = MediaAnalysisStatus.Failed,
                AnalyzedVersion = state.AnalyzedVersion,
                AttemptCount = attemptCount,
                LastAttemptAt = DateTimeOffset.UtcNow,
                FailureReason = error,
                ModeStatuses = state.ModeStatuses,
            };
            await failedState.Save(ct);

            logger.LogError(
                "Media analysis for entity {EntityId} permanently failed after {Retries} retries: {Error}",
                state.EntityId, attemptCount, error);
        }
        else
        {
            var retryState = new MediaAnalysisState<TEntity>
            {
                Id = state.Id!,
                EntityId = state.EntityId,
                Status = MediaAnalysisStatus.Queued,
                AnalyzedVersion = state.AnalyzedVersion,
                AttemptCount = attemptCount,
                LastAttemptAt = DateTimeOffset.UtcNow,
                FailureReason = error,
                ModeStatuses = state.ModeStatuses,
            };
            await retryState.Save(ct);

            var delay = CalculateRetryDelay(attemptCount);
            logger.LogWarning(
                "Media analysis for entity {EntityId} failed (retry {Retry}/{MaxRetries}), will retry in {Delay}: {Error}",
                state.EntityId, attemptCount, options.Value.MaxRetries, delay, error);
        }
    }

    private TimeSpan CalculateRetryDelay(int retryCount)
    {
        var delay = options.Value.InitialRetryDelay.TotalSeconds *
                    Math.Pow(options.Value.RetryBackoffMultiplier, retryCount - 1);

        var clampedDelay = Math.Min(delay, options.Value.MaxRetryDelay.TotalSeconds);
        return TimeSpan.FromSeconds(clampedDelay);
    }
}
