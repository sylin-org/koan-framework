namespace Koan.Data.Backup.Models;

/// <summary>
/// Configuration options for the Koan.Data.Backup module
/// </summary>
public class BackupRestoreOptions
{
    /// <summary>
    /// Default storage profile to use for backups when not specified
    /// </summary>
    public string? DefaultStorageProfile { get; set; }

    /// <summary>
    /// Default batch size for entity streaming operations
    /// </summary>
    public int DefaultBatchSize { get; set; } = 1000;

    /// <summary>
    /// Whether to warm up entity discovery on application startup
    /// </summary>
    public bool WarmupEntitiesOnStartup { get; set; } = false;

    /// <summary>
    /// Whether to enable background maintenance services
    /// </summary>
    public bool EnableBackgroundMaintenance { get; set; } = false;

    /// <summary>
    /// Interval for background maintenance operations
    /// </summary>
    public TimeSpan MaintenanceInterval { get; set; } = TimeSpan.FromHours(6);

    /// <summary>
    /// Backup retention policy settings
    /// </summary>
    public BackupRetentionPolicy? RetentionPolicy { get; set; }

    /// <summary>
    /// Maximum concurrency for backup operations
    /// </summary>
    public int MaxConcurrency { get; set; } = 4;

    /// <summary>
    /// Whether to validate backups automatically after creation
    /// </summary>
    public bool AutoValidateBackups { get; set; } = true;

    /// <summary>
    /// Default compression level for backup archives
    /// </summary>
    public System.IO.Compression.CompressionLevel CompressionLevel { get; set; } = System.IO.Compression.CompressionLevel.Optimal;
}

/// <summary>
/// Backup retention policy configuration
/// </summary>
public class BackupRetentionPolicy
{
    /// <summary>
    /// Number of daily backups to keep
    /// </summary>
    public int KeepDaily { get; set; } = 7;

    /// <summary>
    /// Number of weekly backups to keep
    /// </summary>
    public int KeepWeekly { get; set; } = 4;

    /// <summary>
    /// Number of monthly backups to keep
    /// </summary>
    public int KeepMonthly { get; set; } = 12;

    /// <summary>
    /// Number of yearly backups to keep
    /// </summary>
    public int KeepYearly { get; set; } = 3;

    /// <summary>
    /// Tags that should be excluded from automatic cleanup
    /// </summary>
    public string[] ExcludeFromCleanup { get; set; } = new[] { "permanent", "archive", "compliance" };
}