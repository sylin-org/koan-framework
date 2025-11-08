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
    private const int MaxRetries = 5;
    private const int PollIntervalMs = 5000; // 5 seconds

    public VectorSyncWorker(ILogger<VectorSyncWorker> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
        // Query pending operations (not using partition context - outbox table is global)
        var pendingOps = await SyncOperation.Query(
            op => op.Status == OperationStatus.Pending && op.RetryCount < MaxRetries,
            cancellationToken);

        var opsList = pendingOps.ToList();

        if (opsList.Count == 0)
            return;

        _logger.LogDebug("Processing {Count} pending vector operations", opsList.Count);

        foreach (var operation in opsList)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await ProcessOperationAsync(operation, cancellationToken);
        }

        _logger.LogInformation(
            "Processed {Count} vector operations ({Succeeded} succeeded, {Failed} failed)",
            opsList.Count,
            opsList.Count(o => o.Status == OperationStatus.Completed),
            opsList.Count(o => o.Status == OperationStatus.DeadLetter));
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

            _logger.LogDebug(
                "Processing vector operation {OpId} for chunk {ChunkId} (attempt {Attempt}/{Max})",
                operation.Id,
                operation.ChunkId,
                operation.RetryCount + 1,
                MaxRetries);

            // Deserialize embedding and metadata
            var embedding = operation.GetEmbedding();
            var metadata = operation.GetMetadata<object>();

            // Set partition context for this chunk's project
            using (EntityContext.Partition(operation.PartitionId))
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
                }

                await job.Save(cancellationToken);
            }
            else
            {
                _logger.LogWarning(
                    "Job {JobId} not found for operation {OpId} - operation completed but job may have been deleted",
                    operation.JobId,
                    operation.Id);
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
