using Koan.Data.Abstractions;
using Koan.Data.Backup.Abstractions;
using Koan.Data.Backup.Models;
using Koan.Data.Backup.Storage;
using Koan.Data.Core;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;

namespace Koan.Data.Backup.Core;

public class StreamingBackupService : IBackupService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IEntityDiscoveryService _discoveryService;
    private readonly BackupStorageService _storageService;
    private readonly ILogger<StreamingBackupService> _logger;
    private readonly ConcurrentDictionary<string, BackupProgress> _activeBackups = new();

    public StreamingBackupService(
        IServiceProvider serviceProvider,
        IEntityDiscoveryService discoveryService,
        BackupStorageService storageService,
        ILogger<StreamingBackupService> logger)
    {
        _serviceProvider = serviceProvider;
        _discoveryService = discoveryService;
        _storageService = storageService;
        _logger = logger;
    }

    public async Task<BackupManifest> BackupEntityAsync<TEntity, TKey>(
        string backupName,
        BackupOptions? options = null,
        CancellationToken ct = default)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        options ??= new BackupOptions();
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation("Starting backup of {EntityType} to {BackupName}", typeof(TEntity).Name, backupName);

        var manifest = new BackupManifest
        {
            Id = Guid.CreateVersion7().ToString(),
            Name = backupName,
            Description = options.Description ?? $"Backup of {typeof(TEntity).Name}",
            Tags = options.Tags,
            CreatedAt = DateTimeOffset.UtcNow,
            Status = BackupStatus.InProgress,
            Version = "1.0"
        };

        try
        {
            // Track progress
            var progress = new BackupProgress
            {
                BackupId = manifest.Id,
                Status = BackupStatus.InProgress,
                TotalEntities = 1,
                CurrentEntityType = typeof(TEntity).Name
            };
            _activeBackups[manifest.Id] = progress;

            // Create backup archive
            var (archiveStream, backupPath) = await _storageService.CreateBackupArchiveAsync(
                backupName, options.StorageProfile, ct);

            using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create);

            // Get entity stream using Data<> AllStream
            var entityStream = GetEntityStream<TEntity, TKey>(options.BatchSize);

            // Store entity data
            var entityInfo = await _storageService.StoreEntityDataAsync(
                archive,
                typeof(TEntity).Name,
                typeof(TKey).Name,
                GetEntityProvider<TEntity, TKey>(),
                entityStream,
                options.Set ?? "root",
                ct);

            entityInfo.BackupDuration = stopwatch.Elapsed;
            manifest.Entities.Add(entityInfo);

            // Update progress
            progress.EntitiesCompleted = 1;
            progress.BytesProcessed = entityInfo.SizeBytes;
            progress.ElapsedTime = stopwatch.Elapsed;

            // Finalize manifest
            manifest.CompletedAt = DateTimeOffset.UtcNow;
            manifest.Duration = stopwatch.Elapsed;
            manifest.Status = BackupStatus.Completed;

            // Set verification data
            manifest.Verification.TotalItemCount = entityInfo.ItemCount;
            manifest.Verification.TotalSizeBytes = entityInfo.SizeBytes;
            manifest.Verification.OverallChecksum = _storageService.ComputeOverallChecksum(manifest.Entities);

            // Store manifest and verification
            await _storageService.StoreManifestAsync(archive, manifest, ct);
            await _storageService.StoreVerificationAsync(archive, manifest, ct);

            // Upload to storage
            archive.Dispose(); // Close the archive
            var contentHash = await _storageService.UploadBackupArchiveAsync(
                archiveStream, backupPath, options.StorageProfile, ct);

            manifest.Verification.OverallChecksum = contentHash;

            // Update final progress
            progress.Status = BackupStatus.Completed;
            progress.ElapsedTime = stopwatch.Elapsed;

            _logger.LogInformation("Completed backup of {EntityType} in {Duration}ms. {ItemCount} items, {SizeKB} KB",
                typeof(TEntity).Name, stopwatch.ElapsedMilliseconds, entityInfo.ItemCount, entityInfo.SizeBytes / 1024);

            return manifest;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Backup failed for {EntityType}", typeof(TEntity).Name);
            manifest.Status = BackupStatus.Failed;
            manifest.CompletedAt = DateTimeOffset.UtcNow;
            manifest.Duration = stopwatch.Elapsed;

            if (_activeBackups.TryGetValue(manifest.Id, out var progress))
            {
                progress.Status = BackupStatus.Failed;
                progress.ElapsedTime = stopwatch.Elapsed;
            }

            throw;
        }
        finally
        {
            _activeBackups.TryRemove(manifest.Id, out _);
        }
    }

    public async Task<BackupManifest> BackupAllEntitiesAsync(
        string backupName,
        GlobalBackupOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new GlobalBackupOptions();
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation("Starting backup of all entities to {BackupName}", backupName);

        // Ensure all entities are discovered and warmed up
        await _discoveryService.WarmupAllEntitiesAsync(ct);

        var allEntities = _discoveryService.GetDiscoveredEntities()
            .Where(e => ShouldIncludeEntity(e, options))
            .ToList();

        var manifest = new BackupManifest
        {
            Id = Guid.CreateVersion7().ToString(),
            Name = backupName,
            Description = options.Description ?? $"Backup of {allEntities.Count} entity types",
            Tags = options.Tags,
            CreatedAt = DateTimeOffset.UtcNow,
            Status = BackupStatus.InProgress,
            Version = "1.0"
        };

        // Set discovery info
        manifest.Discovery = new EntityDiscoveryInfo
        {
            DiscoveredAt = DateTimeOffset.UtcNow,
            TotalEntityTypes = allEntities.Count,
            TotalProviders = allEntities.Select(e => e.Provider).Distinct().Count(),
            EntitiesByProvider = allEntities.GroupBy(e => e.Provider).ToDictionary(g => g.Key, g => g.Count())
        };

        try
        {
            // Track progress
            var progress = new BackupProgress
            {
                BackupId = manifest.Id,
                Status = BackupStatus.InProgress,
                TotalEntities = allEntities.Count
            };
            _activeBackups[manifest.Id] = progress;

            // Create backup archive
            var (archiveStream, backupPath) = await _storageService.CreateBackupArchiveAsync(
                backupName, options.StorageProfile, ct);

            using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create);

            // Backup entities sequentially to avoid ZIP archive concurrency issues
            var results = new List<EntityBackupInfo?>();
            foreach (var entityInfo in allEntities)
            {
                ct.ThrowIfCancellationRequested();

                var result = await BackupEntityByReflection(archive, entityInfo, options, ct);
                results.Add(result);

                // Update progress
                progress.EntitiesCompleted++;
                progress.CurrentEntityType = entityInfo.EntityType.Name;
                progress.ElapsedTime = stopwatch.Elapsed;

                _logger.LogDebug("Completed backup of {EntityType} ({Completed}/{Total})",
                    entityInfo.EntityType.Name, progress.EntitiesCompleted, allEntities.Count);
            }
            manifest.Entities = results.Where(r => r != null).ToList()!;

            // Calculate totals
            manifest.Verification.TotalItemCount = manifest.Entities.Sum(e => e.ItemCount);
            manifest.Verification.TotalSizeBytes = manifest.Entities.Sum(e => e.SizeBytes);
            manifest.Verification.OverallChecksum = _storageService.ComputeOverallChecksum(manifest.Entities);

            // Finalize manifest
            manifest.CompletedAt = DateTimeOffset.UtcNow;
            manifest.Duration = stopwatch.Elapsed;
            manifest.Status = BackupStatus.Completed;

            // Store manifest and verification
            await _storageService.StoreManifestAsync(archive, manifest, ct);
            await _storageService.StoreVerificationAsync(archive, manifest, ct);

            // Upload to storage
            archive.Dispose();
            var contentHash = await _storageService.UploadBackupArchiveAsync(
                archiveStream, backupPath, options.StorageProfile, ct);

            // Update final progress
            progress.Status = BackupStatus.Completed;
            progress.BytesProcessed = manifest.Verification.TotalSizeBytes;
            progress.ElapsedTime = stopwatch.Elapsed;

            _logger.LogInformation("Completed backup of {EntityCount} entity types in {Duration}ms. {ItemCount} total items, {SizeKB} KB",
                manifest.Entities.Count, stopwatch.ElapsedMilliseconds,
                manifest.Verification.TotalItemCount, manifest.Verification.TotalSizeBytes / 1024);

            return manifest;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Global backup failed");
            manifest.Status = BackupStatus.Failed;
            manifest.CompletedAt = DateTimeOffset.UtcNow;
            manifest.Duration = stopwatch.Elapsed;

            if (_activeBackups.TryGetValue(manifest.Id, out var progress))
            {
                progress.Status = BackupStatus.Failed;
                progress.ElapsedTime = stopwatch.Elapsed;
            }

            throw;
        }
        finally
        {
            _activeBackups.TryRemove(manifest.Id, out _);
        }
    }

    public async Task<BackupManifest> BackupSelectedAsync(
        string backupName,
        Func<EntityTypeInfo, bool> filter,
        GlobalBackupOptions? options = null,
        CancellationToken ct = default)
    {
        await _discoveryService.WarmupAllEntitiesAsync(ct);
        var allEntities = _discoveryService.GetDiscoveredEntities().Where(filter).ToList();

        var modifiedOptions = options ?? new GlobalBackupOptions();
        modifiedOptions.IncludeEntityTypes = allEntities.Select(e => e.EntityType.Name).ToArray();

        return await BackupAllEntitiesAsync(backupName, modifiedOptions, ct);
    }

    public async Task<BackupManifest> BackupByProviderAsync(
        string backupName,
        string[] providers,
        GlobalBackupOptions? options = null,
        CancellationToken ct = default)
    {
        return await BackupSelectedAsync(backupName, entity => providers.Contains(entity.Provider), options, ct);
    }

    public Task<BackupProgress> GetBackupProgressAsync(string backupId, CancellationToken ct = default)
    {
        _activeBackups.TryGetValue(backupId, out var progress);
        return Task.FromResult(progress ?? new BackupProgress { BackupId = backupId, Status = BackupStatus.Completed });
    }

    public Task CancelBackupAsync(string backupId, CancellationToken ct = default)
    {
        if (_activeBackups.TryGetValue(backupId, out var progress))
        {
            progress.Status = BackupStatus.Cancelled;
        }
        return Task.CompletedTask;
    }

    private async Task<EntityBackupInfo?> BackupEntityByReflection(
        ZipArchive archive,
        EntityTypeInfo entityInfo,
        GlobalBackupOptions options,
        CancellationToken ct)
    {
        try
        {
            var stopwatch = Stopwatch.StartNew();

            // Get entity stream using reflection
            var entityStream = AggregateConfigsExtensions.GetAllStreamByReflection(
                entityInfo.EntityType, entityInfo.KeyType, options.BatchSize, ct);

            // Store entity data
            var backupInfo = await _storageService.StoreEntityDataAsync(
                archive,
                entityInfo.EntityType.Name,
                entityInfo.KeyType.Name,
                entityInfo.Provider,
                entityStream,
                "root", // TODO: support different sets
                ct);

            backupInfo.BackupDuration = stopwatch.Elapsed;
            return backupInfo;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to backup entity type {EntityType}", entityInfo.EntityType.Name);
            return new EntityBackupInfo
            {
                EntityType = entityInfo.EntityType.Name,
                KeyType = entityInfo.KeyType.Name,
                Provider = entityInfo.Provider,
                ErrorMessage = ex.Message
            };
        }
    }

    private IAsyncEnumerable<TEntity> GetEntityStream<TEntity, TKey>(int batchSize)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        return Data<TEntity, TKey>.AllStream(batchSize);
    }

    private string GetEntityProvider<TEntity, TKey>()
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        return AggregateConfigs.Get<TEntity, TKey>(_serviceProvider).Provider;
    }

    private static bool ShouldIncludeEntity(EntityTypeInfo entity, GlobalBackupOptions options)
    {
        if (options.IncludeProviders?.Any() == true && !options.IncludeProviders.Contains(entity.Provider))
            return false;

        if (options.ExcludeProviders?.Any() == true && options.ExcludeProviders.Contains(entity.Provider))
            return false;

        if (options.IncludeEntityTypes?.Any() == true && !options.IncludeEntityTypes.Contains(entity.EntityType.Name))
            return false;

        if (options.ExcludeEntityTypes?.Any() == true && options.ExcludeEntityTypes.Contains(entity.EntityType.Name))
            return false;

        return true;
    }
}