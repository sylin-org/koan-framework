using Koan.Data.Abstractions;
using Koan.Data.Backup.Models;

namespace Koan.Data.Backup.Abstractions;

public interface IRestoreService
{
    /// <summary>
    /// Restores a specific entity type from backup
    /// </summary>
    Task RestoreEntityAsync<TEntity, TKey>(string backupName, RestoreOptions? options = null, CancellationToken ct = default)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull;

    /// <summary>
    /// Restores all entities from a backup
    /// </summary>
    Task RestoreAllEntitiesAsync(string backupName, GlobalRestoreOptions? options = null, CancellationToken ct = default);

    /// <summary>
    /// Restores selected entities based on filter
    /// </summary>
    Task RestoreSelectedAsync(string backupName, Func<EntityBackupInfo, bool> filter, GlobalRestoreOptions? options = null, CancellationToken ct = default);

    /// <summary>
    /// Tests restore viability without actually restoring
    /// </summary>
    Task<RestoreViabilityReport> TestRestoreViabilityAsync(string backupName, CancellationToken ct = default);

    /// <summary>
    /// Gets restore progress for monitoring
    /// </summary>
    Task<RestoreProgress> GetRestoreProgressAsync(string restoreId, CancellationToken ct = default);

    /// <summary>
    /// Cancels an ongoing restore operation
    /// </summary>
    Task CancelRestoreAsync(string restoreId, CancellationToken ct = default);
}

public class RestoreViabilityReport
{
    public string BackupName { get; set; } = default!;
    public bool IsViable { get; set; }
    public List<string> Issues { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public Dictionary<string, EntityRestoreViability> EntityViability { get; set; } = new();
    public TimeSpan EstimatedRestoreTime { get; set; }
    public long EstimatedMemoryUsage { get; set; }
}

public class EntityRestoreViability
{
    public string EntityType { get; set; } = default!;
    public bool CanRestore { get; set; }
    public string? Issue { get; set; }
    public bool AdapterOptimizationAvailable { get; set; }
    public double EstimatedSpeedup { get; set; } = 1.0;
}

public class RestoreProgress
{
    public string RestoreId { get; set; } = default!;
    public RestoreStatus Status { get; set; }
    public int EntitiesCompleted { get; set; }
    public int TotalEntities { get; set; }
    public long RecordsRestored { get; set; }
    public long TotalEstimatedRecords { get; set; }
    public TimeSpan ElapsedTime { get; set; }
    public TimeSpan? EstimatedTimeRemaining { get; set; }
    public string? CurrentEntityType { get; set; }
    public double PercentComplete => TotalEntities > 0 ? (double)EntitiesCompleted / TotalEntities * 100 : 0;
}

public enum RestoreStatus
{
    Created,
    Preparing,
    InProgress,
    Completed,
    Failed,
    Cancelled
}