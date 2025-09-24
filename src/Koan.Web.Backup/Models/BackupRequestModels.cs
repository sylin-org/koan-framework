using System.ComponentModel.DataAnnotations;
using System.IO.Compression;

namespace Koan.Web.Backup.Models;

/// <summary>
/// Request model for creating a new backup
/// </summary>
public class CreateBackupRequest
{
    /// <summary>
    /// Name of the backup (required)
    /// </summary>
    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Optional description of the backup
    /// </summary>
    [StringLength(1000)]
    public string? Description { get; set; }

    /// <summary>
    /// Tags to apply to the backup
    /// </summary>
    public string[] Tags { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Target set name (optional, defaults to "root")
    /// </summary>
    [StringLength(100)]
    public string? Set { get; set; }

    /// <summary>
    /// Storage profile to use (optional, uses default if not specified)
    /// </summary>
    [StringLength(100)]
    public string? StorageProfile { get; set; }

    /// <summary>
    /// Compression level (Optimal, Fastest, NoCompression, SmallestSize)
    /// </summary>
    public CompressionLevel CompressionLevel { get; set; } = CompressionLevel.Optimal;

    /// <summary>
    /// Whether to enable verification after backup
    /// </summary>
    public bool VerificationEnabled { get; set; } = true;

    /// <summary>
    /// Batch size for streaming operations
    /// </summary>
    [Range(100, 10000)]
    public int BatchSize { get; set; } = 1000;

    /// <summary>
    /// Additional metadata for the backup
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = new();
}

/// <summary>
/// Request model for global backup operations
/// </summary>
public class CreateGlobalBackupRequest : CreateBackupRequest
{
    /// <summary>
    /// Maximum concurrency for backup operations
    /// </summary>
    [Range(1, 20)]
    public int MaxConcurrency { get; set; } = Environment.ProcessorCount;

    /// <summary>
    /// Include only specific providers
    /// </summary>
    public string[]? IncludeProviders { get; set; }

    /// <summary>
    /// Exclude specific providers
    /// </summary>
    public string[]? ExcludeProviders { get; set; }

    /// <summary>
    /// Include only specific entity types
    /// </summary>
    public string[]? IncludeEntityTypes { get; set; }

    /// <summary>
    /// Exclude specific entity types
    /// </summary>
    public string[]? ExcludeEntityTypes { get; set; }

    /// <summary>
    /// Whether to include entities with no data
    /// </summary>
    public bool IncludeEmptyEntities { get; set; } = false;

    /// <summary>
    /// Maximum entity size in bytes
    /// </summary>
    public long MaxEntitySizeBytes { get; set; } = long.MaxValue;

    /// <summary>
    /// Operation timeout
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromHours(2);
}

/// <summary>
/// Request model for restore operations
/// </summary>
public class RestoreBackupRequest
{
    /// <summary>
    /// Name or ID of the backup to restore
    /// </summary>
    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string BackupName { get; set; } = string.Empty;

    /// <summary>
    /// Target set name (optional)
    /// </summary>
    [StringLength(100)]
    public string? TargetSet { get; set; }

    /// <summary>
    /// Storage profile to use for reading backup
    /// </summary>
    [StringLength(100)]
    public string? StorageProfile { get; set; }

    /// <summary>
    /// Whether to replace existing entities
    /// </summary>
    public bool ReplaceExisting { get; set; } = false;

    /// <summary>
    /// Whether to disable constraints during restore
    /// </summary>
    public bool DisableConstraints { get; set; } = true;

    /// <summary>
    /// Whether to disable indexes during restore
    /// </summary>
    public bool DisableIndexes { get; set; } = true;

    /// <summary>
    /// Whether to use bulk mode for faster restore
    /// </summary>
    public bool UseBulkMode { get; set; } = true;

    /// <summary>
    /// Batch size for restore operations
    /// </summary>
    [Range(100, 10000)]
    public int BatchSize { get; set; } = 1000;

    /// <summary>
    /// Optimization level (Fast, Balanced, Safe)
    /// </summary>
    [StringLength(20)]
    public string OptimizationLevel { get; set; } = "Balanced";

    /// <summary>
    /// Whether to perform a dry run (validation only)
    /// </summary>
    public bool DryRun { get; set; } = false;

    /// <summary>
    /// Whether to continue on errors
    /// </summary>
    public bool ContinueOnError { get; set; } = false;

    /// <summary>
    /// Operation timeout
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromHours(2);
}

/// <summary>
/// Request model for global restore operations
/// </summary>
public class RestoreGlobalBackupRequest : RestoreBackupRequest
{
    /// <summary>
    /// Maximum concurrency for restore operations
    /// </summary>
    [Range(1, 20)]
    public int MaxConcurrency { get; set; } = Environment.ProcessorCount;

    /// <summary>
    /// Include only specific entity types
    /// </summary>
    public string[]? IncludeEntityTypes { get; set; }

    /// <summary>
    /// Exclude specific entity types
    /// </summary>
    public string[]? ExcludeEntityTypes { get; set; }

    /// <summary>
    /// Entity set mapping (EntityType -> TargetSet)
    /// </summary>
    public Dictionary<string, string> EntitySetMapping { get; set; } = new();

    /// <summary>
    /// Whether to restore to original sets
    /// </summary>
    public bool RestoreToOriginalSets { get; set; } = true;

    /// <summary>
    /// Whether to validate backup before restore
    /// </summary>
    public bool ValidateBeforeRestore { get; set; } = true;
}

/// <summary>
/// Request model for entity-specific backup operations
/// </summary>
public class EntityBackupRequest : CreateBackupRequest
{
    /// <summary>
    /// Entity type name (e.g., "User", "Order")
    /// </summary>
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string EntityType { get; set; } = string.Empty;
}

/// <summary>
/// Request model for entity-specific restore operations
/// </summary>
public class EntityRestoreRequest : RestoreBackupRequest
{
    /// <summary>
    /// Entity type name (e.g., "User", "Order")
    /// </summary>
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string EntityType { get; set; } = string.Empty;
}