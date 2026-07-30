namespace Koan.Data.AI;

/// <summary>
/// Configuration options for the EmbeddingWorker background service.
/// Part of ARCH-0070: Attribute-Driven AI Embeddings (Phase 3).
/// </summary>
public class EmbeddingWorkerOptions
{
    /// <summary>
    /// Batch size for processing jobs (default: 10)
    /// </summary>
    public int BatchSize { get; set; } = 10;

    /// <summary>
    /// Poll interval when jobs are available (default: 1 second)
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Poll interval when no jobs are available (default: 5 seconds)
    /// </summary>
    public TimeSpan IdlePollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Global rate limit: maximum embeddings per minute (default: 60)
    /// Set to 0 to disable global rate limiting
    /// </summary>
    public int GlobalRateLimitPerMinute { get; set; } = 60;

    /// <summary>
    /// Maximum retry attempts for failed jobs (default: 3)
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Retry delay backoff multiplier (default: 2.0)
    /// Retry delays: 1s, 2s, 4s, 8s, etc.
    /// </summary>
    public double RetryBackoffMultiplier { get; set; } = 2.0;

    /// <summary>
    /// Initial retry delay (default: 1 second)
    /// </summary>
    public TimeSpan InitialRetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Maximum retry delay (default: 5 minutes)
    /// </summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Duration of a processing claim before another worker may recover it. Active workers renew the lease while
    /// the embedding provider is running. The same duration is the recovery grace for legacy Processing rows that
    /// predate durable ownership metadata.
    /// </summary>
    public TimeSpan ProcessingLeaseDuration { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Enable automatic cleanup of completed jobs (default: true)
    /// </summary>
    public bool AutoCleanupCompleted { get; set; } = true;

    /// <summary>
    /// Age threshold for auto-cleanup of completed jobs (default: 24 hours)
    /// </summary>
    public TimeSpan CompletedJobRetention { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Enable the worker (default: true)
    /// Set to false to disable background processing
    /// </summary>
    public bool Enabled { get; set; } = true;
}
