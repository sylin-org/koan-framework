using Koan.Data.Backup.Abstractions;
using Koan.Data.Backup.Models;

namespace Koan.Web.Backup.Models;

/// <summary>
/// Response model for backup operations
/// </summary>
public class BackupOperationResponse
{
    /// <summary>
    /// Unique operation ID for tracking progress
    /// </summary>
    public string OperationId { get; set; } = string.Empty;

    /// <summary>
    /// Name of the backup being created
    /// </summary>
    public string BackupName { get; set; } = string.Empty;

    /// <summary>
    /// Current status of the operation
    /// </summary>
    public BackupOperationStatus Status { get; set; }

    /// <summary>
    /// Started timestamp
    /// </summary>
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>
    /// Estimated completion time (if available)
    /// </summary>
    public DateTimeOffset? EstimatedCompletionAt { get; set; }

    /// <summary>
    /// Progress information
    /// </summary>
    public BackupProgressInfo? Progress { get; set; }

    /// <summary>
    /// Error message if operation failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Backup manifest when operation completes successfully
    /// </summary>
    public BackupManifest? Result { get; set; }

    /// <summary>
    /// URL to check operation status
    /// </summary>
    public string StatusUrl { get; set; } = string.Empty;

    /// <summary>
    /// URL to cancel the operation (if still running)
    /// </summary>
    public string? CancelUrl { get; set; }
}

/// <summary>
/// Response model for restore operations
/// </summary>
public class RestoreOperationResponse
{
    /// <summary>
    /// Unique operation ID for tracking progress
    /// </summary>
    public string OperationId { get; set; } = string.Empty;

    /// <summary>
    /// Name of the backup being restored
    /// </summary>
    public string BackupName { get; set; } = string.Empty;

    /// <summary>
    /// Current status of the operation
    /// </summary>
    public RestoreOperationStatus Status { get; set; }

    /// <summary>
    /// Started timestamp
    /// </summary>
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>
    /// Estimated completion time (if available)
    /// </summary>
    public DateTimeOffset? EstimatedCompletionAt { get; set; }

    /// <summary>
    /// Progress information
    /// </summary>
    public RestoreProgressInfo? Progress { get; set; }

    /// <summary>
    /// Error message if operation failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Restore result when operation completes successfully
    /// </summary>
    public RestoreResult? Result { get; set; }

    /// <summary>
    /// URL to check operation status
    /// </summary>
    public string StatusUrl { get; set; } = string.Empty;

    /// <summary>
    /// URL to cancel the operation (if still running)
    /// </summary>
    public string? CancelUrl { get; set; }
}

/// <summary>
/// Progress information for backup operations
/// </summary>
public class BackupProgressInfo
{
    /// <summary>
    /// Overall progress percentage (0-100)
    /// </summary>
    public double PercentComplete { get; set; }

    /// <summary>
    /// Current stage of the backup process
    /// </summary>
    public string CurrentStage { get; set; } = string.Empty;

    /// <summary>
    /// Total number of entities to backup
    /// </summary>
    public int TotalEntityTypes { get; set; }

    /// <summary>
    /// Number of entity types completed
    /// </summary>
    public int CompletedEntityTypes { get; set; }

    /// <summary>
    /// Current entity type being processed
    /// </summary>
    public string? CurrentEntityType { get; set; }

    /// <summary>
    /// Total number of entity instances processed
    /// </summary>
    public long TotalItemsProcessed { get; set; }

    /// <summary>
    /// Estimated total number of items
    /// </summary>
    public long? EstimatedTotalItems { get; set; }

    /// <summary>
    /// Total bytes processed
    /// </summary>
    public long TotalBytesProcessed { get; set; }

    /// <summary>
    /// Processing rate (items per second)
    /// </summary>
    public double ItemsPerSecond { get; set; }

    /// <summary>
    /// Processing rate (bytes per second)
    /// </summary>
    public double BytesPerSecond { get; set; }

    /// <summary>
    /// Elapsed time since start
    /// </summary>
    public TimeSpan ElapsedTime { get; set; }

    /// <summary>
    /// Estimated time remaining
    /// </summary>
    public TimeSpan? EstimatedTimeRemaining { get; set; }
}

/// <summary>
/// Progress information for restore operations
/// </summary>
public class RestoreProgressInfo
{
    /// <summary>
    /// Overall progress percentage (0-100)
    /// </summary>
    public double PercentComplete { get; set; }

    /// <summary>
    /// Current stage of the restore process
    /// </summary>
    public string CurrentStage { get; set; } = string.Empty;

    /// <summary>
    /// Total number of entities to restore
    /// </summary>
    public int TotalEntityTypes { get; set; }

    /// <summary>
    /// Number of entity types completed
    /// </summary>
    public int CompletedEntityTypes { get; set; }

    /// <summary>
    /// Current entity type being processed
    /// </summary>
    public string? CurrentEntityType { get; set; }

    /// <summary>
    /// Total number of entity instances restored
    /// </summary>
    public long TotalItemsRestored { get; set; }

    /// <summary>
    /// Total number of items in backup
    /// </summary>
    public long TotalItemsInBackup { get; set; }

    /// <summary>
    /// Total bytes processed
    /// </summary>
    public long TotalBytesProcessed { get; set; }

    /// <summary>
    /// Processing rate (items per second)
    /// </summary>
    public double ItemsPerSecond { get; set; }

    /// <summary>
    /// Processing rate (bytes per second)
    /// </summary>
    public double BytesPerSecond { get; set; }

    /// <summary>
    /// Elapsed time since start
    /// </summary>
    public TimeSpan ElapsedTime { get; set; }

    /// <summary>
    /// Estimated time remaining
    /// </summary>
    public TimeSpan? EstimatedTimeRemaining { get; set; }

    /// <summary>
    /// Number of errors encountered (if continuing on error)
    /// </summary>
    public int ErrorCount { get; set; }

    /// <summary>
    /// Recent error messages
    /// </summary>
    public string[] RecentErrors { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Backup catalog response with pagination
/// </summary>
public class BackupCatalogResponse
{
    /// <summary>
    /// List of backup information
    /// </summary>
    public BackupInfo[] Backups { get; set; } = Array.Empty<BackupInfo>();

    /// <summary>
    /// Total number of backups matching the query
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// Current page number (1-based)
    /// </summary>
    public int Page { get; set; }

    /// <summary>
    /// Number of items per page
    /// </summary>
    public int PageSize { get; set; }

    /// <summary>
    /// Total number of pages
    /// </summary>
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);

    /// <summary>
    /// Whether there are more pages
    /// </summary>
    public bool HasNextPage => Page < TotalPages;

    /// <summary>
    /// Whether there are previous pages
    /// </summary>
    public bool HasPreviousPage => Page > 1;
}

/// <summary>
/// System status response
/// </summary>
public class BackupSystemStatusResponse
{
    /// <summary>
    /// Current system status
    /// </summary>
    public BackupSystemStatus Status { get; set; }

    /// <summary>
    /// Number of discovered entity types
    /// </summary>
    public int DiscoveredEntityTypes { get; set; }

    /// <summary>
    /// Number of active backup operations
    /// </summary>
    public int ActiveBackupOperations { get; set; }

    /// <summary>
    /// Number of active restore operations
    /// </summary>
    public int ActiveRestoreOperations { get; set; }

    /// <summary>
    /// Available storage profiles
    /// </summary>
    public string[] AvailableStorageProfiles { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Total storage used by backups (in bytes)
    /// </summary>
    public long? TotalBackupStorageBytes { get; set; }

    /// <summary>
    /// Last discovery time
    /// </summary>
    public DateTimeOffset? LastDiscoveryTime { get; set; }

    /// <summary>
    /// System health indicators
    /// </summary>
    public Dictionary<string, object> HealthIndicators { get; set; } = new();
}

/// <summary>
/// Operation status enumeration
/// </summary>
public enum BackupOperationStatus
{
    /// <summary>
    /// Operation is queued but not started
    /// </summary>
    Queued,

    /// <summary>
    /// Operation is currently running
    /// </summary>
    Running,

    /// <summary>
    /// Operation completed successfully
    /// </summary>
    Completed,

    /// <summary>
    /// Operation failed with errors
    /// </summary>
    Failed,

    /// <summary>
    /// Operation was cancelled
    /// </summary>
    Cancelled,

    /// <summary>
    /// Operation completed with warnings
    /// </summary>
    CompletedWithWarnings
}

/// <summary>
/// Restore operation status enumeration
/// </summary>
public enum RestoreOperationStatus
{
    /// <summary>
    /// Operation is queued but not started
    /// </summary>
    Queued,

    /// <summary>
    /// Validating backup before restore
    /// </summary>
    Validating,

    /// <summary>
    /// Preparing adapters for restore
    /// </summary>
    Preparing,

    /// <summary>
    /// Operation is currently running
    /// </summary>
    Running,

    /// <summary>
    /// Finalizing and restoring normal operation
    /// </summary>
    Finalizing,

    /// <summary>
    /// Operation completed successfully
    /// </summary>
    Completed,

    /// <summary>
    /// Operation failed with errors
    /// </summary>
    Failed,

    /// <summary>
    /// Operation was cancelled
    /// </summary>
    Cancelled,

    /// <summary>
    /// Operation completed with warnings
    /// </summary>
    CompletedWithWarnings
}

/// <summary>
/// System status enumeration
/// </summary>
public enum BackupSystemStatus
{
    /// <summary>
    /// System is healthy and ready
    /// </summary>
    Healthy,

    /// <summary>
    /// System is initializing
    /// </summary>
    Initializing,

    /// <summary>
    /// System has warnings but is operational
    /// </summary>
    Warning,

    /// <summary>
    /// System has errors or is degraded
    /// </summary>
    Error,

    /// <summary>
    /// System is unavailable
    /// </summary>
    Unavailable
}

/// <summary>
/// Result of a restore operation
/// </summary>
public class RestoreResult
{
    /// <summary>
    /// Total number of entities restored
    /// </summary>
    public long TotalItemsRestored { get; set; }

    /// <summary>
    /// Number of entity types restored
    /// </summary>
    public int EntityTypesRestored { get; set; }

    /// <summary>
    /// Total bytes processed
    /// </summary>
    public long TotalBytesProcessed { get; set; }

    /// <summary>
    /// Duration of the operation
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Number of errors encountered
    /// </summary>
    public int ErrorCount { get; set; }

    /// <summary>
    /// Detailed results per entity type
    /// </summary>
    public EntityRestoreResult[] EntityResults { get; set; } = Array.Empty<EntityRestoreResult>();
}

/// <summary>
/// Restore result for a specific entity type
/// </summary>
public class EntityRestoreResult
{
    /// <summary>
    /// Entity type name
    /// </summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>
    /// Number of items restored
    /// </summary>
    public long ItemsRestored { get; set; }

    /// <summary>
    /// Number of items skipped
    /// </summary>
    public long ItemsSkipped { get; set; }

    /// <summary>
    /// Number of errors
    /// </summary>
    public int ErrorCount { get; set; }

    /// <summary>
    /// Duration for this entity
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Whether adapter optimization was used
    /// </summary>
    public bool OptimizationUsed { get; set; }
}