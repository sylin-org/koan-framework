using Koan.Context.Models;
using Koan.Data.Core;
using Koan.Data.Vector;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Koan.Context.Services;

/// <summary>
/// Background service that processes pending vector operations from the outbox table
/// </summary>
/// <remarks>
/// Implements the Transactional Outbox Pattern for reliable dual-store coordination.
///
/// Responsibilities:
/// - Poll SyncOperation table every 5 seconds
/// - Process pending operations (Status == Pending, RetryCount &lt; 5)
/// - Retry failed operations with exponential backoff
/// - Move permanently failed operations to dead-letter queue
///
/// This ensures at-least-once delivery even if the vector store is temporarily unavailable.
/// </remarks>
public class VectorSyncWorker : BackgroundService
{
    private readonly ILogger<VectorSyncWorker> _logger;
    private readonly MetricsCollector _metricsCollector;
    private readonly HashSet<string> _deletedJobIds = new();
    private const int MaxRetries = 5;
    private const int PollIntervalMs = 5000; // 5 seconds

    public VectorSyncWorker(
        ILogger<VectorSyncWorker> logger,
        MetricsCollector metricsCollector)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metricsCollector = metricsCollector ?? throw new ArgumentNullException(nameof(metricsCollector));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("VectorSyncWorker started - polling every {Interval}ms", PollIntervalMs);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingOperationsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing pending vector operations");
            }

            await Task.Delay(PollIntervalMs, stoppingToken);
        }

        _logger.LogInformation("VectorSyncWorker stopped");
    }

    private async Task ProcessPendingOperationsAsync(CancellationToken cancellationToken)
    {
        // Clear the deleted jobs cache if it gets too large (prevent unbounded growth)
        if (_deletedJobIds.Count > 1000)
        {
            _logger.LogInformation("Clearing deleted jobs cache ({Count} entries)", _deletedJobIds.Count);
            _deletedJobIds.Clear();
        }

        // Query pending operations (not using partition context - outbox table is global)
        var pendingOps = await SyncOperation.Query(
            op => op.Status == OperationStatus.Pending && op.RetryCount < MaxRetries,
            cancellationToken);

        var opsList = pendingOps.ToList();

        if (opsList.Count == 0)
            return;

        _logger.LogDebug("Processing {Count} pending vector operations", opsList.Count);

        var successCount = 0;
        var failureCount = 0;

        foreach (var operation in opsList)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var beforeStatus = operation.Status;
            await ProcessOperationAsync(operation, cancellationToken);

            if (operation.Status == OperationStatus.Completed && beforeStatus != OperationStatus.Completed)
            {
                successCount++;
            }
            else if (operation.Status == OperationStatus.DeadLetter)
            {
                failureCount++;
            }
        }

        _logger.LogInformation(
            "Processed {Count} vector operations ({Succeeded} succeeded, {Failed} failed)",
            opsList.Count,
            opsList.Count(o => o.Status == OperationStatus.Completed),
            opsList.Count(o => o.Status == OperationStatus.DeadLetter));

        // Record outbox processing metrics
        if (successCount > 0)
        {
            _metricsCollector.RecordOutboxProcessed(successCount, true);
        }
        if (failureCount > 0)
        {
            _metricsCollector.RecordOutboxProcessed(failureCount, false);
        }
    }

    private async Task ProcessOperationAsync(SyncOperation operation, CancellationToken cancellationToken)
    {
        try
        {
            // Check if this is an orphaned operation (no JobId - from old implementation)
            if (string.IsNullOrWhiteSpace(operation.JobId))
            {
                _logger.LogWarning(
                    "Orphaned vector operation {OpId} for chunk {ChunkId} (no JobId) - marking as completed and deleting",
                    operation.Id,
                    operation.ChunkId);

                // Delete orphaned operation
                await operation.Delete(cancellationToken);
                return;
            }

            // Fast-fail if job is known to be deleted - don't waste resources processing
            if (_deletedJobIds.Contains(operation.JobId))
            {
                _logger.LogDebug(
                    "Skipping vector sync for operation {OpId} - job {JobId} is cached as deleted, deleting operation",
                    operation.Id,
                    operation.JobId);

                // Delete the operation since the parent job no longer exists
                await operation.Delete(cancellationToken);
                return;
            }

            _logger.LogDebug(
                "Processing vector operation {OpId} for chunk {ChunkId} (attempt {Attempt}/{Max})",
                operation.Id,
                operation.ChunkId,
                operation.RetryCount + 1,
                MaxRetries);

            // Deserialize embedding and metadata
            var embedding = operation.GetEmbedding();
            var metadata = operation.GetMetadata<object>();

            // Set partition context for this chunk's project (adapters handle formatting)
            using (EntityContext.Partition(operation.ProjectId))
            {
                // Save to vector store within partition context
                var batch = new List<(string Id, float[] Embedding, object? Metadata)>
                {
                    (operation.ChunkId, embedding, metadata)
                };

                await Vector<Chunk>.Save(batch, cancellationToken);
            }

            // Mark as completed (outside partition context, in root table)
            operation.MarkCompleted();
            await operation.Save(cancellationToken);

            // Increment job's VectorsSynced counter (in root context)
            // Fast-fail if we know the job has been deleted
            if (_deletedJobIds.Contains(operation.JobId))
            {
                // Job is in the deleted cache - skip lookup
                _logger.LogDebug(
                    "Skipping job update for operation {OpId} - job {JobId} is known to be deleted (cached)",
                    operation.Id,
                    operation.JobId);
            }
            else
            {
                var job = await Job.Get(operation.JobId, cancellationToken);
                if (job != null)
                {
                    job.VectorsSynced++;

                    // Update ETA based on composite progress (chunking + vector syncing)
                    job.UpdateVectorSyncProgress();

                    _logger.LogDebug(
                        "Job {JobId} progress: {VectorsSynced}/{ChunksCreated} vectors synced",
                        job.Id,
                        job.VectorsSynced,
                        job.ChunksCreated);

                    // Check if all vectors have been synced to Weaviate
                    if (job.VectorsSynced >= job.ChunksCreated && job.ChunksCreated > 0)
                    {
                        // Job is complete - all chunks created and all vectors synced
                        job.Complete();
                        _logger.LogInformation(
                            "Job {JobId} completed: {ChunksCreated} chunks created, {VectorsSynced} vectors synced to Weaviate",
                            job.Id,
                            job.ChunksCreated,
                            job.VectorsSynced);

                        // Record job completion metrics
                        _metricsCollector.RecordJobCompleted(
                            job.Id,
                            job.ProjectId,
                            job.Elapsed.TotalSeconds,
                            true,
                            job.ProcessedFiles,
                            job.ChunksCreated);
                    }

                    await job.Save(cancellationToken);
                }
                else
                {
                    // Job not found - add to cache to avoid future lookups
                    _deletedJobIds.Add(operation.JobId);
                    _logger.LogWarning(
                        "Job {JobId} not found for operation {OpId} - operation completed but job may have been deleted (caching for fast-fail)",
                        operation.JobId,
                        operation.Id);
                }
            }

            _logger.LogDebug(
                "Successfully synced vector for chunk {ChunkId} (operation {OpId})",
                operation.ChunkId,
                operation.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to sync vector for chunk {ChunkId} (operation {OpId}, attempt {Attempt}/{Max})",
                operation.ChunkId,
                operation.Id,
                operation.RetryCount + 1,
                MaxRetries);

            operation.RecordFailure(ex.Message);
            await operation.Save(cancellationToken);

            if (operation.Status == OperationStatus.DeadLetter)
            {
                _logger.LogError(
                    "Vector operation {OpId} moved to dead-letter queue after {Retries} failed attempts. " +
                    "Chunk {ChunkId} metadata is saved but vector is missing.",
                    operation.Id,
                    operation.RetryCount,
                    operation.ChunkId);
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("VectorSyncWorker stopping - processing remaining operations");

        // Give pending operations a chance to complete
        await ProcessPendingOperationsAsync(cancellationToken);

        await base.StopAsync(cancellationToken);
    }
}
