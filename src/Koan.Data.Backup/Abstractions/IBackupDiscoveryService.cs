using Koan.Data.Backup.Models;

namespace Koan.Data.Backup.Abstractions;

public interface IBackupDiscoveryService
{
    /// <summary>
    /// Discovers all available backups across all storage profiles
    /// </summary>
    Task<BackupCatalog> DiscoverAllBackupsAsync(DiscoveryOptions? options = null, CancellationToken ct = default);

    /// <summary>
    /// Discovers backups in a specific storage profile
    /// </summary>
    Task<BackupCatalog> DiscoverByStorageProfileAsync(string storageProfile, CancellationToken ct = default);

    /// <summary>
    /// Queries backups using filter criteria
    /// </summary>
    Task<BackupCatalog> QueryBackupsAsync(BackupQuery query, CancellationToken ct = default);

    /// <summary>
    /// Gets a specific backup by ID
    /// </summary>
    Task<BackupInfo?> GetBackupAsync(string backupId, CancellationToken ct = default);

    /// <summary>
    /// Validates a backup's integrity
    /// </summary>
    Task<BackupValidationResult> ValidateBackupAsync(string backupId, CancellationToken ct = default);

    /// <summary>
    /// Refreshes the backup catalog cache
    /// </summary>
    Task RefreshCatalogAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets catalog statistics
    /// </summary>
    Task<BackupCatalogStats> GetCatalogStatsAsync(CancellationToken ct = default);
}

public class BackupCatalog
{
    public List<BackupInfo> Backups { get; set; } = new();
    public DateTimeOffset DiscoveredAt { get; set; }
    public int TotalCount { get; set; }
    public BackupCatalogStats Stats { get; set; } = new();
    public BackupQuery? Query { get; set; }
    public TimeSpan DiscoveryDuration { get; set; }
}

public class BackupInfo
{
    public string Id { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string Description { get; set; } = string.Empty;
    public string[] Tags { get; set; } = Array.Empty<string>();
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public BackupStatus Status { get; set; }
    public long SizeBytes { get; set; }
    public int EntityCount { get; set; }
    public string StorageProfile { get; set; } = default!;
    public string[]? EntityTypes { get; set; }
    public string[]? Providers { get; set; }
    public BackupHealthStatus? HealthStatus { get; set; }
    public DateTimeOffset? LastValidatedAt { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();
}

public class BackupQuery
{
    public string[]? Tags { get; set; }
    public string[]? EntityTypes { get; set; }
    public string[]? StorageProfiles { get; set; }
    public DateTimeOffset? DateFrom { get; set; }
    public DateTimeOffset? DateTo { get; set; }
    public BackupStatus[]? Statuses { get; set; }
    public BackupHealthStatus[]? HealthStatuses { get; set; }
    public string? SearchTerm { get; set; }
    public long? MinSizeBytes { get; set; }
    public long? MaxSizeBytes { get; set; }
    public string? SortBy { get; set; }
    public SortDirection SortDirection { get; set; } = SortDirection.Descending;
    public int Skip { get; set; }
    public int Take { get; set; } = 100;
}

public class DiscoveryOptions
{
    public bool UseFastPath { get; set; } = true;
    public bool FullScan { get; set; } = false;
    public TimeSpan CacheExpiry { get; set; } = TimeSpan.FromMinutes(5);
    public int MaxConcurrency { get; set; } = Environment.ProcessorCount;
    public string[]? StorageProfiles { get; set; }
}

public class BackupCatalogStats
{
    public int TotalBackups { get; set; }
    public long TotalSizeBytes { get; set; }
    public int HealthyBackups { get; set; }
    public int BackupsRequiringAttention { get; set; }
    public Dictionary<string, int> BackupsByStatus { get; set; } = new();
    public Dictionary<string, int> BackupsByProvider { get; set; } = new();
    public Dictionary<string, long> SizeByStorageProfile { get; set; } = new();
    public DateTimeOffset? OldestBackup { get; set; }
    public DateTimeOffset? NewestBackup { get; set; }
}

public class BackupValidationResult
{
    public string BackupId { get; set; } = default!;
    public bool IsValid { get; set; }
    public List<string> Issues { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public BackupHealthStatus HealthStatus { get; set; }
    public DateTimeOffset ValidatedAt { get; set; }
    public TimeSpan ValidationDuration { get; set; }
}

public enum BackupHealthStatus
{
    Unknown,
    Healthy,
    Warning,
    Critical,
    Corrupted
}

public enum SortDirection
{
    Ascending,
    Descending
}