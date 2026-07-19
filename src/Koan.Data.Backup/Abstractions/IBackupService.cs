using Koan.Data.Abstractions;
using Koan.Data.Backup.Models;

namespace Koan.Data.Backup.Abstractions;

/// <summary>
/// Creates and restores integrity-checked archives for one Entity type.
/// </summary>
public interface IBackupService
{
    Task<BackupReceipt> Create<TEntity, TKey>(
        string name,
        BackupRequest? request = null,
        CancellationToken ct = default)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull;

    Task<RestoreReceipt> Restore<TEntity, TKey>(
        string storageKey,
        RestoreRequest? request = null,
        CancellationToken ct = default)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull;
}
