namespace S5.Recs.Services;

/// <summary>
/// Orchestrates partition-based import pipeline.
/// Replaces monolithic SeedService with job-based async workflow.
/// Part of ARCH-0069: Partition-Based Import Pipeline Architecture.
/// </summary>
public interface IImportOrchestrator
{
    /// <summary>
    /// Queues import jobs for the specified media types from a provider source.
    /// Returns immediately with job IDs for progress tracking.
    /// </summary>
    /// <param name="source">Provider source code (e.g., "Anilist")</param>
    /// <param name="mediaTypeIds">Media type IDs to import (or "all")</param>
    /// <param name="options">Import options (limit, overwrite)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of created job IDs</returns>
    Task<List<string>> QueueImportAsync(
        string source,
        string[] mediaTypeIds,
        ImportOptions options,
        CancellationToken ct);

    /// <summary>
    /// Gets progress for specified import jobs.
    /// Progress is derived from media counts in each partition.
    /// </summary>
    /// <param name="jobIds">Job IDs to query</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Progress information for each job</returns>
    Task<ImportProgressResponse> GetProgressAsync(
        string[] jobIds,
        CancellationToken ct);
}

/// <summary>
/// Import options for job configuration
/// </summary>
public record ImportOptions(
    int? Limit = null,
    bool Overwrite = false);

/// <summary>
/// Progress response containing job status information
/// </summary>
public record ImportProgressResponse(
    List<JobProgress> Jobs);

/// <summary>
/// Individual job progress information
/// </summary>
public record JobProgress(
    string JobId,
    string Source,
    string MediaType,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    PartitionCounts Counts,
    List<string> Errors);

/// <summary>
/// Media counts by partition stage
/// </summary>
public record PartitionCounts(
    int InRaw,
    int InQueue,
    int Completed,
    double PercentComplete);
