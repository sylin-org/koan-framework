using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Context.Models;
using Koan.Data.Core;
using Koan.Data.Vector;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Koan.Context.Services;

/// <summary>
/// Background service that processes pending vector snapshots captured during indexing.
/// </summary>
public sealed class VectorSyncWorker : BackgroundService
{
    private const int PollIntervalMs = 5000;
    private const int MaxRetries = 5;
    private const int MaxBatchSize = 50;

    private readonly ILogger<VectorSyncWorker> _logger;
    private readonly MetricsCollector _metricsCollector;
    private readonly HashSet<string> _deletedJobIds = new(StringComparer.Ordinal);

    public VectorSyncWorker(
        ILogger<VectorSyncWorker> logger,
        MetricsCollector metricsCollector)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metricsCollector = metricsCollector ?? throw new ArgumentNullException(nameof(metricsCollector));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("VectorSyncWorker started – polling every {Interval} ms", PollIntervalMs);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingSnapshotsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "VectorSyncWorker encountered an error while processing snapshots");
            }

            try
            {
                await Task.Delay(PollIntervalMs, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("VectorSyncWorker stopped");
    }

    private async Task ProcessPendingSnapshotsAsync(CancellationToken cancellationToken)
    {
        if (_deletedJobIds.Count > 1024)
        {
            _deletedJobIds.Clear();
            _logger.LogDebug("Cleared deleted job cache after reaching 1024 entries");
        }

        var pendingStates = await ChunkVectorState.Query(
            state => (state.State == VectorSyncState.Pending ||
                      (state.State == VectorSyncState.Failed && state.AttemptCount < MaxRetries)),
            cancellationToken);

        var snapshotBatch = pendingStates
            .OrderBy(state => state.AttemptCount)
            .ThenBy(state => state.LastAttemptAt ?? state.CreatedAt)
            .ThenBy(state => state.UpdatedAt)
            .Take(MaxBatchSize)
            .ToList();

        if (snapshotBatch.Count == 0)
        {
            return;
        }

        _logger.LogDebug("Processing {Count} pending vector snapshot(s)", snapshotBatch.Count);

        var succeeded = 0;
        var permanentlyFailed = 0;

        foreach (var snapshot in snapshotBatch)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var previousState = snapshot.State;

            await ProcessSnapshotAsync(snapshot, cancellationToken);

            if (snapshot.State == VectorSyncState.Synced && previousState != VectorSyncState.Synced)
            {
                succeeded++;
            }
            else if (snapshot.State == VectorSyncState.Failed && snapshot.AttemptCount >= MaxRetries)
            {
                permanentlyFailed++;
            }
        }

        if (succeeded > 0)
        {
            _metricsCollector.RecordVectorProcessing(succeeded, success: true);
        }

        if (permanentlyFailed > 0)
        {
            _metricsCollector.RecordVectorProcessing(permanentlyFailed, success: false);
        }
    }

    private async Task ProcessSnapshotAsync(ChunkVectorState snapshot, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(snapshot.JobId))
        {
            _logger.LogWarning(
                "Removing snapshot for chunk {ChunkId} because it is missing a job identifier",
                snapshot.ChunkId);
            await snapshot.Remove(cancellationToken);
            return;
        }

        if (_deletedJobIds.Contains(snapshot.JobId))
        {
            _logger.LogDebug(
                "Skipping snapshot for chunk {ChunkId} because job {JobId} no longer exists",
                snapshot.ChunkId,
                snapshot.JobId);
            await snapshot.Remove(cancellationToken);
            return;
        }

        try
        {
            if (snapshot.State == VectorSyncState.Failed)
            {
                snapshot.PrepareRetry();
                await snapshot.Save(cancellationToken);
            }

            using (EntityContext.Partition(snapshot.ProjectId))
            {
                var payload = new List<(string Id, float[] Embedding, object? Metadata)>
                {
                    (snapshot.ChunkId, snapshot.GetEmbedding(), snapshot.GetMetadata<ChunkVectorMetadata>())
                };

                await Vector<Chunk>.Save(payload, cancellationToken);
            }

            snapshot.MarkSynced();
            await snapshot.Save(cancellationToken);

            await UpdateJobProgressAsync(snapshot.JobId, cancellationToken);
        }
        catch (Exception ex)
        {
            snapshot.RecordFailure(ex.Message, MaxRetries);
            await snapshot.Save(cancellationToken);

            if (snapshot.State == VectorSyncState.Failed && snapshot.AttemptCount >= MaxRetries)
            {
                _logger.LogError(
                    ex,
                    "Vector sync for chunk {ChunkId} failed permanently after {Attempts} attempts",
                    snapshot.ChunkId,
                    snapshot.AttemptCount);
            }
            else
            {
                _logger.LogWarning(
                    ex,
                    "Vector sync attempt {Attempt}/{Max} failed for chunk {ChunkId}",
                    snapshot.AttemptCount,
                    MaxRetries,
                    snapshot.ChunkId);
            }

            await UpdateJobProgressAsync(snapshot.JobId, cancellationToken);
        }
    }

    private async Task UpdateJobProgressAsync(string jobId, CancellationToken cancellationToken)
    {
        if (_deletedJobIds.Contains(jobId))
        {
            return;
        }

        var job = await Job.Get(jobId, cancellationToken);
        if (job is null)
        {
            _deletedJobIds.Add(jobId);
            _logger.LogWarning(
                "Job {JobId} not found while updating vector sync progress; caching for fast skip",
                jobId);
            return;
        }

        var snapshots = await ChunkVectorState.Query(state => state.JobId == jobId, cancellationToken);
        var snapshotList = snapshots.ToList();

        var total = snapshotList.Count;
        var synced = snapshotList.Count(state => state.State == VectorSyncState.Synced);
        var failed = snapshotList.Count(state => state.State == VectorSyncState.Failed);

        job.VectorsSaved = total;
        job.VectorsSynced = synced;
        job.UpdateVectorSyncProgress();

        if (failed > 0 && job.Status == JobStatus.Indexing)
        {
            job.CurrentOperation = $"Vector sync retrying ({synced}/{total} succeeded)";
        }

        if (synced >= job.ChunksCreated && job.ChunksCreated > 0 && job.Status != JobStatus.Completed)
        {
            job.Complete();
            _logger.LogInformation(
                "Job {JobId} completed vector synchronization ({ChunksCreated} chunks)",
                job.Id,
                job.ChunksCreated);

            _metricsCollector.RecordJobCompleted(
                job.Id!,
                job.ProjectId,
                job.Elapsed.TotalSeconds,
                success: true,
                job.ProcessedFiles,
                job.ChunksCreated);
        }

        await job.Save(cancellationToken);
    }
}

