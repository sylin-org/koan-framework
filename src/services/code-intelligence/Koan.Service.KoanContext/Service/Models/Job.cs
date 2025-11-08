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
    /// Total vectors saved
    /// </summary>
    public int VectorsSaved { get; set; }

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
    /// Progress percentage (0-100)
    /// </summary>
    public decimal Progress => TotalFiles > 0
        ? Math.Round((decimal)ProcessedFiles / TotalFiles * 100, 2)
        : 0;

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
    /// Marks the job as cancelled
    /// </summary>
    public void Cancel()
    {
        Status = JobStatus.Cancelled;
        CompletedAt = DateTime.UtcNow;
        CurrentOperation = "Cancelled";
    }

    /// <summary>
    /// Updates progress and calculates ETA
    /// </summary>
    public void UpdateProgress(int processed, string? operation = null)
    {
        ProcessedFiles = processed;

        if (!string.IsNullOrEmpty(operation))
            CurrentOperation = operation;

        // Calculate ETA based on current throughput
        if (processed > 0 && TotalFiles > 0)
        {
            var timePerFile = Elapsed.TotalSeconds / processed;
            var remainingFiles = TotalFiles - processed;
            var remainingSeconds = timePerFile * remainingFiles;
            EstimatedCompletion = DateTime.UtcNow.AddSeconds(remainingSeconds);
        }
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
