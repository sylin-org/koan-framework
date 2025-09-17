using Koan.Core;
using Koan.Data.Abstractions;
using Koan.Data.Backup.Abstractions;
using Koan.Data.Backup.Models;
using Koan.Data.Core;
using Koan.Data.Core.Model;

namespace Koan.Data.Backup.Extensions;

/// <summary>
/// Extension methods for Entity<> types to provide simple backup/restore functionality
/// </summary>
public static class EntityBackupExtensions
{
    /// <summary>
    /// Backs up this entity type to the specified backup name
    /// </summary>
    public static Task<BackupManifest> BackupTo<TEntity, TKey>(
        this Entity<TEntity, TKey> entity,
        string backupName,
        string? description = null,
        CancellationToken ct = default)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var backupService = GetBackupService();
        var options = new BackupOptions { Description = description };
        return backupService.BackupEntityAsync<TEntity, TKey>(backupName, options, ct);
    }

    /// <summary>
    /// Backs up this entity type with detailed options
    /// </summary>
    public static Task<BackupManifest> BackupTo<TEntity, TKey>(
        this Entity<TEntity, TKey> entity,
        string backupName,
        BackupOptions options,
        CancellationToken ct = default)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var backupService = GetBackupService();
        return backupService.BackupEntityAsync<TEntity, TKey>(backupName, options, ct);
    }

    /// <summary>
    /// Backs up this entity type with tags and storage profile
    /// </summary>
    public static Task<BackupManifest> BackupTo<TEntity, TKey>(
        this Entity<TEntity, TKey> entity,
        string backupName,
        string? description,
        string[]? tags,
        string? storageProfile = null,
        CancellationToken ct = default)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var backupService = GetBackupService();
        var options = new BackupOptions
        {
            Description = description ?? string.Empty,
            Tags = tags ?? Array.Empty<string>(),
            StorageProfile = storageProfile ?? string.Empty
        };
        return backupService.BackupEntityAsync<TEntity, TKey>(backupName, options, ct);
    }

    /// <summary>
    /// Restores this entity type from the specified backup
    /// </summary>
    public static Task RestoreFrom<TEntity, TKey>(
        this Entity<TEntity, TKey> entity,
        string backupName,
        bool replaceExisting = false,
        CancellationToken ct = default)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var restoreService = GetRestoreService();
        var options = new RestoreOptions { ReplaceExisting = replaceExisting };
        return restoreService.RestoreEntityAsync<TEntity, TKey>(backupName, options, ct);
    }

    /// <summary>
    /// Restores this entity type with detailed options
    /// </summary>
    public static Task RestoreFrom<TEntity, TKey>(
        this Entity<TEntity, TKey> entity,
        string backupName,
        RestoreOptions options,
        CancellationToken ct = default)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var restoreService = GetRestoreService();
        return restoreService.RestoreEntityAsync<TEntity, TKey>(backupName, options, ct);
    }

    /// <summary>
    /// Gets backup information for this entity type
    /// </summary>
    public static async Task<BackupInfo[]> ListBackups<TEntity, TKey>(
        this Entity<TEntity, TKey> entity,
        CancellationToken ct = default)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var discoveryService = GetDiscoveryService();
        var catalog = await discoveryService.DiscoverAllBackupsAsync(ct: ct);
        return catalog.Backups
            .Where(b => b.EntityTypes?.Contains(typeof(TEntity).Name) == true)
            .ToArray();
    }

    /// <summary>
    /// Deletes a backup for this entity type
    /// </summary>
    public static async Task<bool> DeleteBackup<TEntity, TKey>(
        this Entity<TEntity, TKey> entity,
        string backupName,
        CancellationToken ct = default)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        // This would need to be implemented in a backup management service
        await Task.CompletedTask;
        return true; // Placeholder
    }

    /// <summary>
    /// Gets information about a specific backup
    /// </summary>
    public static async Task<BackupInfo?> GetBackupInfo<TEntity, TKey>(
        this Entity<TEntity, TKey> entity,
        string backupName,
        CancellationToken ct = default)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var discoveryService = GetDiscoveryService();
        return await discoveryService.GetBackupAsync(backupName, ct);
    }

    private static IBackupService GetBackupService()
    {
        return Koan.Core.Hosting.App.AppHost.Current?.GetService(typeof(IBackupService)) as IBackupService
            ?? throw new InvalidOperationException("IBackupService not registered. Ensure services.AddKoanBackupRestore() has been called.");
    }

    private static IRestoreService GetRestoreService()
    {
        return Koan.Core.Hosting.App.AppHost.Current?.GetService(typeof(IRestoreService)) as IRestoreService
            ?? throw new InvalidOperationException("IRestoreService not registered. Ensure services.AddKoanBackupRestore() has been called.");
    }

    private static IBackupDiscoveryService GetDiscoveryService()
    {
        return Koan.Core.Hosting.App.AppHost.Current?.GetService(typeof(IBackupDiscoveryService)) as IBackupDiscoveryService
            ?? throw new InvalidOperationException("IBackupDiscoveryService not registered. Ensure services.AddKoanBackupRestore() has been called.");
    }
}

/// <summary>
/// Static backup operations for entity types
/// </summary>
public static class DataBackup
{
    /// <summary>
    /// Backs up an entity type to the specified backup name
    /// </summary>
    public static Task<BackupManifest> BackupTo<TEntity, TKey>(
        string backupName,
        string? description = null,
        CancellationToken ct = default)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var backupService = GetBackupService();
        var options = new BackupOptions { Description = description };
        return backupService.BackupEntityAsync<TEntity, TKey>(backupName, options, ct);
    }

    /// <summary>
    /// Backs up an entity type with detailed options
    /// </summary>
    public static Task<BackupManifest> BackupTo<TEntity, TKey>(
        string backupName,
        BackupOptions options,
        CancellationToken ct = default)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var backupService = GetBackupService();
        return backupService.BackupEntityAsync<TEntity, TKey>(backupName, options, ct);
    }

    /// <summary>
    /// Restores an entity type from the specified backup
    /// </summary>
    public static Task RestoreFrom<TEntity, TKey>(
        string backupName,
        RestoreOptions? options = null,
        CancellationToken ct = default)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var restoreService = GetRestoreService();
        return restoreService.RestoreEntityAsync<TEntity, TKey>(backupName, options, ct);
    }

    /// <summary>
    /// Gets backup information for an entity type
    /// </summary>
    public static async Task<BackupInfo[]> ListBackups<TEntity, TKey>(
        CancellationToken ct = default)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var discoveryService = GetDiscoveryService();
        var catalog = await discoveryService.DiscoverAllBackupsAsync(ct: ct);
        return catalog.Backups
            .Where(b => b.EntityTypes?.Contains(typeof(TEntity).Name) == true)
            .ToArray();
    }

    private static IBackupService GetBackupService()
    {
        return Koan.Core.Hosting.App.AppHost.Current?.GetService(typeof(IBackupService)) as IBackupService
            ?? throw new InvalidOperationException("IBackupService not registered. Ensure services.AddKoanBackupRestore() has been called.");
    }

    private static IRestoreService GetRestoreService()
    {
        return Koan.Core.Hosting.App.AppHost.Current?.GetService(typeof(IRestoreService)) as IRestoreService
            ?? throw new InvalidOperationException("IRestoreService not registered. Ensure services.AddKoanBackupRestore() has been called.");
    }

    private static IBackupDiscoveryService GetDiscoveryService()
    {
        return Koan.Core.Hosting.App.AppHost.Current?.GetService(typeof(IBackupDiscoveryService)) as IBackupDiscoveryService
            ?? throw new InvalidOperationException("IBackupDiscoveryService not registered. Ensure services.AddKoanBackupRestore() has been called.");
    }
}