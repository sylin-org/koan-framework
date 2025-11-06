using Koan.Data.Core.Model;

namespace S5.Recs.Models;

/// <summary>
/// Tracks import job progress and status.
/// Jobs in "jobs-active" partition are currently running.
/// Jobs in default partition are complete (success or failure) and provide audit history.
/// Part of ARCH-0069: Partition-Based Import Pipeline Architecture.
/// </summary>
public class ImportJob : Entity<ImportJob>
{
    /// <summary>
    /// Unique job identifier
    /// </summary>
    public string JobId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Provider source code (e.g., "Anilist", "MyAnimeList")
    /// </summary>
    public required string Source { get; set; }

    /// <summary>
    /// Media type ID being imported
    /// </summary>
    public required string MediaTypeId { get; set; }

    /// <summary>
    /// Media type display name
    /// </summary>
    public required string MediaTypeName { get; set; }

    /// <summary>
    /// Current status of the import job
    /// </summary>
    public ImportJobStatus Status { get; set; } = ImportJobStatus.Running;

    /// <summary>
    /// When the job was created
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// When the job started processing
    /// </summary>
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>
    /// When the job completed (success or failure)
    /// </summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    /// Maximum number of items to import (null = unlimited)
    /// </summary>
    public int? Limit { get; set; }

    /// <summary>
    /// Whether to overwrite existing media items
    /// </summary>
    public bool Overwrite { get; set; }

    /// <summary>
    /// Error log for this job
    /// </summary>
    public List<string> Errors { get; set; } = new();
}

/// <summary>
/// Import job status enumeration
/// </summary>
public enum ImportJobStatus
{
    /// <summary>
    /// Job is currently fetching and staging media
    /// </summary>
    Running,

    /// <summary>
    /// Job completed successfully (import phase)
    /// Media items are now in pipeline for vectorization
    /// </summary>
    Completed,

    /// <summary>
    /// Job failed with unrecoverable error
    /// </summary>
    Failed
}
