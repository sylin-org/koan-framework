using Koan.Data.Abstractions;
using Koan.Data.Backup.Models;

namespace Koan.Data.Backup.Abstractions;

public interface IBackupService
{
    /// <summary>
    /// Backs up a specific entity type
    /// </summary>
    Task<BackupManifest> BackupEntityAsync<TEntity, TKey>(string backupName, BackupOptions? options = null, CancellationToken ct = default)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull;

    /// <summary>
    /// Backs up all discovered entity types
    /// </summary>
    Task<BackupManifest> BackupAllEntitiesAsync(string backupName, GlobalBackupOptions? options = null, CancellationToken ct = default);

    /// <summary>
    /// Backs up selected entities based on filter
    /// </summary>
    Task<BackupManifest> BackupSelectedAsync(string backupName, Func<EntityTypeInfo, bool> filter, GlobalBackupOptions? options = null, CancellationToken ct = default);

    /// <summary>
    /// Backs up entities from specific providers
    /// </summary>
    Task<BackupManifest> BackupByProviderAsync(string backupName, string[] providers, GlobalBackupOptions? options = null, CancellationToken ct = default);

    /// <summary>
    /// Gets backup progress for monitoring
    /// </summary>
    Task<BackupProgress> GetBackupProgressAsync(string backupId, CancellationToken ct = default);

    /// <summary>
    /// Cancels an ongoing backup operation
    /// </summary>
    Task CancelBackupAsync(string backupId, CancellationToken ct = default);
}

public class BackupProgress
{
    public string BackupId { get; set; } = default!;
    public BackupStatus Status { get; set; }
    public int EntitiesCompleted { get; set; }
    public int TotalEntities { get; set; }
    public long BytesProcessed { get; set; }
    public long TotalEstimatedBytes { get; set; }
    public TimeSpan ElapsedTime { get; set; }
    public TimeSpan? EstimatedTimeRemaining { get; set; }
    public string? CurrentEntityType { get; set; }
    public double PercentComplete => TotalEntities > 0 ? (double)EntitiesCompleted / TotalEntities * 100 : 0;
}