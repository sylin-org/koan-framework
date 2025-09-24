using Koan.Data.Abstractions;
using Koan.Data.Backup.Abstractions;
using Koan.Data.Backup.Models;
using Koan.Data.Backup.Storage;
using Koan.Data.Core;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Koan.Data.Backup.Core;

public class OptimizedRestoreService : IRestoreService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly BackupStorageService _storageService;
    private readonly IBackupDiscoveryService _discoveryService;
    private readonly ILogger<OptimizedRestoreService> _logger;
    private readonly ConcurrentDictionary<string, Abstractions.RestoreProgress> _activeRestores = new();

    public OptimizedRestoreService(
        IServiceProvider serviceProvider,
        BackupStorageService storageService,
        IBackupDiscoveryService discoveryService,
        ILogger<OptimizedRestoreService> logger)
    {
        _serviceProvider = serviceProvider;
        _storageService = storageService;
        _discoveryService = discoveryService;
        _logger = logger;
    }

    public async Task RestoreEntityAsync<TEntity, TKey>(
        string backupName,
        RestoreOptions? options = null,
        CancellationToken ct = default)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        options ??= new RestoreOptions();
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation("Starting restore of {EntityType} from {BackupName}", typeof(TEntity).Name, backupName);

        var repository = AggregateConfigs.Get<TEntity, TKey>(_serviceProvider).Repository;
        RestorePreparationContext? preparationContext = null;

        try
        {
            // Prepare adapter for bulk operations if supported
            if (repository is IRestoreOptimizedRepository<TEntity, TKey> optimizedRepo)
            {
                var prepOptions = new RestorePreparationOptions
                {
                    EstimatedEntityCount = await EstimateEntityCount<TEntity>(backupName, options.StorageProfile, ct),
                    DisableConstraints = options.DisableConstraints,
                    DisableIndexes = options.DisableIndexes,
                    UseBulkMode = options.UseBulkMode,
                    OptimizationLevel = options.OptimizationLevel
                };

                preparationContext = await optimizedRepo.PrepareForRestoreAsync(prepOptions, ct);
                _logger.LogInformation("Prepared {EntityType} for optimized restore ({OptimizationLevel})",
                    typeof(TEntity).Name, options.OptimizationLevel);
            }

            // Load and restore entities
            await RestoreEntityData<TEntity, TKey>(backupName, options, ct);

            _logger.LogInformation("Completed restore of {EntityType} in {Duration}ms",
                typeof(TEntity).Name, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Restore failed for {EntityType}", typeof(TEntity).Name);
            throw;
        }
        finally
        {
            // Restore normal adapter operation
            if (preparationContext != null && repository is IRestoreOptimizedRepository<TEntity, TKey> optimizedRepo)
            {
                try
                {
                    await optimizedRepo.RestoreNormalOperationAsync(preparationContext, ct);
                    _logger.LogInformation("Restored normal operation for {EntityType}", typeof(TEntity).Name);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to restore normal operation for {EntityType}", typeof(TEntity).Name);
                }
            }
        }
    }

    public async Task RestoreAllEntitiesAsync(
        string backupName,
        GlobalRestoreOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new GlobalRestoreOptions();
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation("Starting restore of all entities from {BackupName}", backupName);

        // Load backup manifest to get entity list
        var manifest = await LoadBackupManifest(backupName, options.StorageProfile, ct);

        var entitiesToRestore = manifest.Entities
            .Where(e => ShouldIncludeEntity(e, options))
            .ToList();

        var restoreId = Guid.CreateVersion7().ToString();
        var progress = new Abstractions.RestoreProgress
        {
            RestoreId = restoreId,
            Status = Abstractions.RestoreStatus.InProgress,
            TotalEntities = entitiesToRestore.Count,
            TotalEstimatedRecords = entitiesToRestore.Sum(e => e.ItemCount)
        };
        _activeRestores[restoreId] = progress;

        try
        {
            if (options.ValidateBeforeRestore)
            {
                _logger.LogInformation("Validating backup before restore...");
                var viabilityReport = await TestRestoreViabilityAsync(backupName, ct);
                if (!viabilityReport.IsViable)
                {
                    throw new InvalidOperationException($"Backup is not viable for restore: {string.Join(", ", viabilityReport.Issues)}");
                }
            }

            // Restore entities with controlled concurrency
            var semaphore = new SemaphoreSlim(options.MaxConcurrency);
            var restoreTasks = entitiesToRestore.Select(async entityInfo =>
            {
                await semaphore.WaitAsync(ct);
                try
                {
                    await RestoreEntityByReflection(entityInfo, backupName, options, ct);
                    progress.EntitiesCompleted++;
                    progress.CurrentEntityType = entityInfo.EntityType;
                    progress.ElapsedTime = stopwatch.Elapsed;
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(restoreTasks);

            progress.Status = Abstractions.RestoreStatus.Completed;
            progress.ElapsedTime = stopwatch.Elapsed;

            _logger.LogInformation("Completed restore of {EntityCount} entity types in {Duration}ms",
                entitiesToRestore.Count, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Global restore failed");
            progress.Status = Abstractions.RestoreStatus.Failed;
            progress.ElapsedTime = stopwatch.Elapsed;
            throw;
        }
        finally
        {
            _activeRestores.TryRemove(restoreId, out _);
        }
    }

    public async Task RestoreSelectedAsync(
        string backupName,
        Func<EntityBackupInfo, bool> filter,
        GlobalRestoreOptions? options = null,
        CancellationToken ct = default)
    {
        // Load manifest and filter entities
        var manifest = await LoadBackupManifest(backupName, options?.StorageProfile ?? string.Empty, ct);
        var selectedEntities = manifest.Entities.Where(filter).ToList();

        var modifiedOptions = options ?? new GlobalRestoreOptions();
        modifiedOptions.IncludeEntityTypes = selectedEntities.Select(e => e.EntityType).ToArray();

        await RestoreAllEntitiesAsync(backupName, modifiedOptions, ct);
    }

    public async Task<Abstractions.RestoreViabilityReport> TestRestoreViabilityAsync(string backupName, CancellationToken ct = default)
    {
        var report = new Abstractions.RestoreViabilityReport
        {
            BackupName = backupName
        };

        try
        {
            // Load and validate backup manifest
            var manifest = await LoadBackupManifest(backupName, string.Empty, ct);

            var issues = new List<string>();
            var warnings = new List<string>();
            var entityViability = new Dictionary<string, Abstractions.EntityRestoreViability>();

            foreach (var entityInfo in manifest.Entities)
            {
                var viability = new Abstractions.EntityRestoreViability
                {
                    EntityType = entityInfo.EntityType,
                    CanRestore = true
                };

                try
                {
                    // Check if entity type still exists
                    var entityType = Type.GetType($"{entityInfo.EntityType}, {entityInfo.EntityType}") ??
                                   AppDomain.CurrentDomain.GetAssemblies()
                                       .SelectMany(a => a.GetTypes())
                                       .FirstOrDefault(t => t.Name == entityInfo.EntityType);

                    if (entityType == null)
                    {
                        viability.CanRestore = false;
                        viability.Issue = "Entity type no longer exists";
                        issues.Add($"Entity type {entityInfo.EntityType} not found");
                    }
                    else
                    {
                        // Check if repository supports optimization
                        var keyType = entityType.GetInterfaces()
                            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEntity<>))
                            ?.GetGenericArguments().FirstOrDefault();

                        if (keyType != null)
                        {
                            var repository = AggregateConfigsExtensions.GetRepositoryByReflection(entityType, keyType, _serviceProvider);
                            if (repository.GetType().GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRestoreOptimizedRepository<,>)))
                            {
                                viability.AdapterOptimizationAvailable = true;
                                viability.EstimatedSpeedup = 5.0; // Estimate based on adapter type
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    viability.CanRestore = false;
                    viability.Issue = ex.Message;
                    issues.Add($"Entity {entityInfo.EntityType}: {ex.Message}");
                }

                entityViability[entityInfo.EntityType] = viability;
            }

            report.IsViable = !issues.Any();
            report.Issues = issues;
            report.Warnings = warnings;
            report.EntityViability = entityViability;
            report.EstimatedRestoreTime = TimeSpan.FromMinutes(manifest.Entities.Sum(e => e.ItemCount) / 10000.0); // Rough estimate
        }
        catch (Exception ex)
        {
            report.IsViable = false;
            report.Issues.Add($"Failed to test viability: {ex.Message}");
        }

        return report;
    }

    public Task<Abstractions.RestoreProgress> GetRestoreProgressAsync(string restoreId, CancellationToken ct = default)
    {
        _activeRestores.TryGetValue(restoreId, out var progress);
        return Task.FromResult(progress ?? new Abstractions.RestoreProgress { RestoreId = restoreId, Status = Abstractions.RestoreStatus.Completed });
    }

    public Task CancelRestoreAsync(string restoreId, CancellationToken ct = default)
    {
        if (_activeRestores.TryGetValue(restoreId, out var progress))
        {
            progress.Status = Abstractions.RestoreStatus.Cancelled;
        }
        return Task.CompletedTask;
    }

    private async Task RestoreEntityData<TEntity, TKey>(
        string backupName,
        RestoreOptions options,
        CancellationToken ct)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var backupPath = await FindBackupPath(backupName, options.StorageProfile, ct);
        using var archive = await _storageService.OpenBackupArchiveAsync(backupPath, options.StorageProfile, ct);

        var manifest = await _storageService.LoadManifestAsync(archive, ct);
        var entityInfo = manifest.Entities.FirstOrDefault(e => e.EntityType == typeof(TEntity).Name);

        if (entityInfo == null)
        {
            throw new InvalidOperationException($"No backup data found for {typeof(TEntity).Name}");
        }

        // Read and restore entities in batches
        var entities = _storageService.ReadEntityDataAsync<TEntity>(archive, entityInfo.StorageFile, ct);
        var batch = new List<TEntity>();
        var batchSize = options.BatchSize;
        var totalRestored = 0;

        await foreach (var entity in entities)
        {
            batch.Add(entity);

            if (batch.Count >= batchSize)
            {
                if (!options.DryRun)
                {
                    await Data<TEntity, TKey>.UpsertManyAsync(batch, ct);
                }
                totalRestored += batch.Count;
                batch.Clear();

                _logger.LogDebug("Restored {Count} {EntityType} entities...", totalRestored, typeof(TEntity).Name);
            }
        }

        // Process final batch
        if (batch.Count > 0)
        {
            if (!options.DryRun)
            {
                await Data<TEntity, TKey>.UpsertManyAsync(batch, ct);
            }
            totalRestored += batch.Count;
        }

        _logger.LogInformation("Successfully restored {Count} {EntityType} entities", totalRestored, typeof(TEntity).Name);
    }

    private async Task RestoreEntityByReflection(
        EntityBackupInfo entityInfo,
        string backupName,
        GlobalRestoreOptions options,
        CancellationToken ct)
    {
        try
        {
            // Find entity type
            var entityType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .FirstOrDefault(t => t.Name == entityInfo.EntityType);

            if (entityType == null)
            {
                _logger.LogWarning("Entity type {EntityType} not found, skipping restore", entityInfo.EntityType);
                return;
            }

            var keyType = entityType.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEntity<>))
                ?.GetGenericArguments().FirstOrDefault();

            if (keyType == null)
            {
                _logger.LogWarning("Could not determine key type for {EntityType}, skipping restore", entityInfo.EntityType);
                return;
            }

            // Load backup data
            var backupPath = await FindBackupPath(backupName, options.StorageProfile, ct);
            using var archive = await _storageService.OpenBackupArchiveAsync(backupPath, options.StorageProfile, ct);

            // Read entities as objects
            var entities = new List<object>();
            await foreach (var entity in ReadEntityDataAsObjects(archive, entityInfo.StorageFile, entityType, ct))
            {
                entities.Add(entity);

                if (entities.Count >= options.BatchSize)
                {
                    if (!options.DryRun)
                    {
                        await AggregateConfigsExtensions.UpsertManyByReflection(entityType, keyType, entities, ct);
                    }
                    entities.Clear();
                }
            }

            // Process final batch
            if (entities.Count > 0 && !options.DryRun)
            {
                await AggregateConfigsExtensions.UpsertManyByReflection(entityType, keyType, entities, ct);
            }

            _logger.LogDebug("Restored entity type {EntityType}", entityInfo.EntityType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restore entity type {EntityType}", entityInfo.EntityType);
            if (!options.ContinueOnError)
                throw;
        }
    }

    private async IAsyncEnumerable<object> ReadEntityDataAsObjects(
        System.IO.Compression.ZipArchive archive,
        string storageFile,
        Type entityType,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var entry = archive.GetEntry(storageFile);
        if (entry == null)
            throw new InvalidOperationException($"Entity data file {storageFile} not found in backup");

        using var entryStream = entry.Open();
        using var reader = new StreamReader(entryStream);

        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(line))
                continue;

            var entity = Newtonsoft.Json.JsonConvert.DeserializeObject(line, entityType);
            if (entity != null)
                yield return entity;
        }
    }

    private async Task<BackupManifest> LoadBackupManifest(string backupName, string storageProfile, CancellationToken ct)
    {
        var backupPath = await FindBackupPath(backupName, storageProfile, ct);
        using var archive = await _storageService.OpenBackupArchiveAsync(backupPath, storageProfile, ct);
        return await _storageService.LoadManifestAsync(archive, ct);
    }

    private async Task<string> FindBackupPath(string backupName, string storageProfile, CancellationToken ct)
    {

        // First try to find by exact backup name
        var backup = await _discoveryService.GetBackupAsync(backupName, ct);

        if (backup != null)
        {
            // Generate the expected backup path based on the backup metadata
            return $"backups/{backup.Name}-{backup.CreatedAt:yyyyMMdd-HHmmss}.zip";
        }

        // If not found by name, try to discover backups in the specified storage profile
        var catalog = await _discoveryService.DiscoverByStorageProfileAsync(storageProfile, ct);
        backup = catalog.Backups.FirstOrDefault(b =>
            b.Name.Equals(backupName, StringComparison.OrdinalIgnoreCase) ||
            b.Id.Equals(backupName, StringComparison.OrdinalIgnoreCase));

        if (backup != null)
        {
            return $"backups/{backup.Name}-{backup.CreatedAt:yyyyMMdd-HHmmss}.zip";
        }

        throw new InvalidOperationException($"Backup '{backupName}' not found in storage profile '{storageProfile}'");
    }

    private async Task<int> EstimateEntityCount<TEntity>(string backupName, string storageProfile, CancellationToken ct)
        where TEntity : class
    {
        try
        {
            var manifest = await LoadBackupManifest(backupName, storageProfile, ct);
            var entityInfo = manifest.Entities.FirstOrDefault(e => e.EntityType == typeof(TEntity).Name);
            return entityInfo?.ItemCount ?? 1000; // Default estimate
        }
        catch
        {
            return 1000; // Default estimate if we can't load manifest
        }
    }

    private static bool ShouldIncludeEntity(EntityBackupInfo entity, GlobalRestoreOptions options)
    {
        if (options.IncludeEntityTypes?.Any() == true && !options.IncludeEntityTypes.Contains(entity.EntityType))
            return false;

        if (options.ExcludeEntityTypes?.Any() == true && options.ExcludeEntityTypes.Contains(entity.EntityType))
            return false;

        return true;
    }
}