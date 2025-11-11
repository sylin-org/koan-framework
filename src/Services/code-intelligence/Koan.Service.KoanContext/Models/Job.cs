using Koan.Data.Abstractions;
using Koan.Data.Core.Model;

namespace Koan.Context.Models;

/// <summary>
/// Represents an indexing job with progress tracking and status
/// </summary>
public class Job : Entity<Job>
{
    /// <summary>
    /// Project being indexed
    /// </summary>
    public string ProjectId { get; set; } = string.Empty;

    /// <summary>
    /// Current job status
    /// </summary>
    public JobStatus Status { get; set; } = JobStatus.Pending;

    /// <summary>
    /// Total number of files to process
    /// </summary>
    public int TotalFiles { get; set; }

    /// <summary>
    /// Files processed so far
    /// </summary>
    public int ProcessedFiles { get; set; }

    /// <summary>
    /// Files skipped (unchanged)
    /// </summary>
    public int SkippedFiles { get; set; }

    /// <summary>
    /// Files with errors
    /// </summary>
    public int ErrorFiles { get; set; }

    /// <summary>
    /// New files discovered
    /// </summary>
    public int NewFiles { get; set; }

    /// <summary>
    /// Changed files detected
    /// </summary>
    public int ChangedFiles { get; set; }

    /// <summary>
    /// Total chunks created/updated
    /// </summary>
    public int ChunksCreated { get; set; }

    /// <summary>
    /// Total vectors saved to outbox (SyncOperations created)
    /// </summary>
    public int VectorsSaved { get; set; }

    /// <summary>
    /// Total vectors successfully synced to Weaviate by VectorSyncWorker
    /// </summary>
    /// <remarks>
    /// Job is complete when VectorsSynced == ChunksCreated.
    /// Tracks actual dual-store synchronization progress.
    /// </remarks>
    public int VectorsSynced { get; set; }

    /// <summary>
    /// When the job started
    /// </summary>
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the job completed (success or failure)
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Estimated completion time
    /// </summary>
    public DateTime? EstimatedCompletion { get; set; }

    /// <summary>
    /// Error message if failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Current operation description
    /// </summary>
    public string? CurrentOperation { get; set; }

    /// <summary>
    /// When the job was cancelled (if applicable)
    /// </summary>
    public DateTime? CancelledAt { get; set; }

    /// <summary>
    /// Non-fatal warnings encountered during indexing (max 100 entries)
    /// </summary>
    public List<string> Warnings { get; set; } = new();

    /// <summary>
    /// Detailed processing log for debugging (max 100 entries, FIFO)
    /// </summary>
    public List<string> ProcessingLog { get; set; } = new();

    private const int MaxLogEntries = 100;

    /// <summary>
    /// Progress percentage (0-100)
    /// </summary>
    /// <remarks>
    /// Composite progress: 50% chunking + 50% vector syncing
    /// - Chunking progress: ProcessedFiles / TotalFiles
    /// - Vector sync progress: VectorsSynced / ChunksCreated
    /// This reflects the parallel processing model where both streams run concurrently.
    /// </remarks>
    public decimal Progress
    {
        get
        {
            if (TotalFiles == 0)
                return 0;

            // Chunking progress (50%)
            var chunkingProgress = (decimal)ProcessedFiles / TotalFiles * 50m;

            // Vector sync progress (50%)
            var vectorProgress = ChunksCreated > 0
                ? (decimal)VectorsSynced / ChunksCreated * 50m
                : 0m;

            return Math.Round(chunkingProgress + vectorProgress, 2);
        }
    }

    /// <summary>
    /// Elapsed time
    /// </summary>
    public TimeSpan Elapsed => CompletedAt.HasValue
        ? CompletedAt.Value - StartedAt
        : DateTime.UtcNow - StartedAt;

    /// <summary>
    /// Creates a new indexing job
    /// </summary>
    public static Job Create(string projectId, int totalFiles)
    {
        return new Job
        {
            ProjectId = projectId,
            Status = JobStatus.Planning,
            TotalFiles = totalFiles,
            StartedAt = DateTime.UtcNow,
            CurrentOperation = "Planning indexing strategy..."
        };
    }

    /// <summary>
    /// Marks the job as completed successfully
    /// </summary>
    public void Complete()
    {
        Status = JobStatus.Completed;
        CompletedAt = DateTime.UtcNow;
        CurrentOperation = "Completed";
    }

    /// <summary>
    /// Marks the job as failed
    /// </summary>
    public void Fail(string errorMessage)
    {
        Status = JobStatus.Failed;
        CompletedAt = DateTime.UtcNow;
        ErrorMessage = errorMessage;
        CurrentOperation = "Failed";
    }

    /// <summary>
    /// Deletes the job and its associated SyncOperations
    /// </summary>
    /// <remarks>
    /// Cascading delete: Removes all SyncOperations linked to this job
    /// to prevent orphaned outbox records.
    /// </remarks>
    public async Task Delete(CancellationToken cancellationToken = default)
    {
        // Delete associated SyncOperations first (cascade delete)
        var operations = await SyncOperation.Query(
            op => op.JobId == Id,
            cancellationToken);

        foreach (var operation in operations)
        {
            await operation.Remove(cancellationToken);
        }

        // Then delete the job itself
        await base.Remove(cancellationToken);
    }

    /// <summary>
    /// Marks the job as cancelled and cleans up associated SyncOperations
    /// </summary>
    public async Task Cancel(CancellationToken cancellationToken = default)
    {
        Status = JobStatus.Cancelled;
        CancelledAt = DateTime.UtcNow;
        CompletedAt = DateTime.UtcNow;
        CurrentOperation = "Cancelled";
        LogOperation("Job cancelled by user");

        // Clean up pending SyncOperations
        var operations = await SyncOperation.Query(
            op => op.JobId == Id && op.Status == OperationStatus.Pending,
            cancellationToken);

        foreach (var operation in operations)
        {
            await operation.Remove(cancellationToken);
        }
    }

    /// <summary>
    /// Updates progress and calculates ETA
    /// </summary>
    public void UpdateProgress(int processed, string? operation = null)
    {
        ProcessedFiles = processed;

        if (!string.IsNullOrEmpty(operation))
            CurrentOperation = operation;

        // Recalculate ETA based on composite progress
        RecalculateEta();
    }

    /// <summary>
    /// Updates vector sync progress and recalculates ETA
    /// </summary>
    /// <remarks>
    /// Called by VectorSyncWorker after each successful vector sync
    /// </remarks>
    public void UpdateVectorSyncProgress()
    {
        RecalculateEta();
    }

    /// <summary>
    /// Recalculates ETA based on composite progress (chunking + vector syncing)
    /// </summary>
    private void RecalculateEta()
    {
        var currentProgress = Progress; // Composite progress: 50% chunking + 50% syncing

        if (currentProgress > 0 && currentProgress < 100)
        {
            // Calculate throughput: progress per second
            var progressPerSecond = currentProgress / (decimal)Elapsed.TotalSeconds;

            if (progressPerSecond > 0)
            {
                var remainingProgress = 100m - currentProgress;
                var remainingSeconds = (double)(remainingProgress / progressPerSecond);
                EstimatedCompletion = DateTime.UtcNow.AddSeconds(remainingSeconds);
            }
        }
        else if (currentProgress >= 100)
        {
            // Job is complete or nearly complete
            EstimatedCompletion = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Adds a non-fatal warning to the job
    /// </summary>
    public void AddWarning(string warning)
    {
        if (Warnings.Count >= MaxLogEntries)
            Warnings.RemoveAt(0); // FIFO - keep most recent

        Warnings.Add($"[{DateTime.UtcNow:HH:mm:ss}] {warning}");
    }

    /// <summary>
    /// Logs a processing operation for debugging (FIFO, max 100 entries)
    /// </summary>
    public void LogOperation(string message)
    {
        if (ProcessingLog.Count >= MaxLogEntries)
            ProcessingLog.RemoveAt(0); // FIFO - keep most recent

        ProcessingLog.Add($"[{DateTime.UtcNow:HH:mm:ss}] {message}");
    }
}

/// <summary>
/// Status of an indexing job
/// </summary>
public enum JobStatus
{
    /// <summary>Job created but not started</summary>
    Pending = 0,

    /// <summary>Computing differential scan plan</summary>
    Planning = 1,

    /// <summary>Actively indexing files</summary>
    Indexing = 2,

    /// <summary>Completed successfully</summary>
    Completed = 3,

    /// <summary>Failed with errors</summary>
    Failed = 4,

    /// <summary>Cancelled by user or system</summary>
    Cancelled = 5
}
