namespace Koan.Data.AI.Options;

/// <summary>
/// Configuration options for the MediaAnalysisWorker background service.
/// Bound from "Koan:Data:AI:MediaAnalysisWorker" configuration section.
/// </summary>
public sealed class MediaAnalysisOptions
{
    /// <summary>
    /// Enable the worker (default: true).
    /// Set to false to disable background media analysis processing.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Batch size for processing items per entity type (default: 10).
    /// </summary>
    public int BatchSize { get; set; } = 10;

    /// <summary>
    /// Poll interval when items are available (default: 2 seconds).
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Poll interval when no items are available (default: 30 seconds).
    /// </summary>
    public TimeSpan IdlePollInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Maximum retry attempts for failed analysis (default: 3).
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Timeout for synchronous analysis processing (default: 60 seconds).
    /// </summary>
    public TimeSpan SyncTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Retry delay backoff multiplier (default: 2.0).
    /// Retry delays: 2s, 4s, 8s, etc.
    /// </summary>
    public double RetryBackoffMultiplier { get; set; } = 2.0;

    /// <summary>
    /// Initial retry delay (default: 2 seconds).
    /// </summary>
    public TimeSpan InitialRetryDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Maximum retry delay (default: 5 minutes).
    /// </summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(5);
}
