# Comprehensive Backup/Restore System Specification for Koan Framework

**Version**: 1.0
**Date**: 2025-01-23
**Status**: Proposal

---

## Executive Summary

This specification defines a comprehensive backup and restore system for Koan Framework applications that leverages existing infrastructure (Data<>, Storage, Aggregates) to provide enterprise-grade data protection with exceptional developer experience.

### Key Innovations

- **Aggregate-Based Discovery**: Enhances existing Aggregate system for automatic entity discovery
- **Streaming-First Architecture**: Leverages `Data<>.AllStream()` and `UpsertMany()` for memory-efficient operations
- **Multi-Storage Support**: Works across all Koan data providers (SQL, NoSQL, Vector, JSON)
- **Adapter-Aware Optimization**: Data adapters can optimize themselves for bulk restore operations
- **Progressive DX**: From one-line backup (`await BackupAll("name")`) to enterprise scenarios

---

## 1. Technical Architecture

### 1.1 Core Infrastructure Leveraged

#### Data Streaming Capabilities

```csharp
// Existing Koan.Data streaming APIs we'll use
IAsyncEnumerable<TEntity> AllStream(int? batchSize = null, CancellationToken ct = default);
Task<int> UpsertMany(IEnumerable<TEntity> models, CancellationToken ct = default);

// WriteCapabilities flags for adapter optimization
[Flags]
public enum WriteCapabilities
{
    BulkUpsert = 1 << 0,
    BulkDelete = 1 << 1,
    AtomicBatch = 1 << 2,
    RestoreOptimization = 1 << 3  // NEW: Adapter supports restore preparation
}
```

#### Enhanced Aggregate System

```csharp
// Build on existing AggregateConfigs for entity discovery
public static class AggregateConfigs
{
    private static readonly ConcurrentDictionary<(Type, Type), object> Cache = new();

    // NEW: Discovery methods for backup system
    public static IEnumerable<EntityTypeInfo> GetAllRegisteredEntities()
    public static void WarmupAllEntities(IServiceProvider sp)
    public static object GetConfigByReflection(Type entityType, Type keyType, IServiceProvider sp)
}
```

### 1.2 New Adapter Preparation Interface

```csharp
/// <summary>
/// Optional interface for data adapters that can optimize themselves for bulk restore operations.
/// Adapters implement this to disable constraints, drop indexes, etc. during restore.
/// </summary>
public interface IRestoreOptimizedRepository<TEntity, TKey> : IDataRepository<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    /// <summary>
    /// Prepare the adapter for efficient bulk restore operations.
    /// Examples: disable foreign key constraints, drop indexes, set bulk insert mode
    /// </summary>
    Task<RestorePreparationContext> PrepareForRestoreAsync(RestorePreparationOptions options, CancellationToken ct = default);

    /// <summary>
    /// Restore normal operation after bulk restore is complete.
    /// Examples: re-enable constraints, rebuild indexes, restore normal mode
    /// </summary>
    Task RestoreNormalOperationAsync(RestorePreparationContext context, CancellationToken ct = default);

    /// <summary>
    /// Get estimated performance improvement from preparation
    /// </summary>
    RestoreOptimizationInfo GetOptimizationInfo();
}

public class RestorePreparationOptions
{
    public int EstimatedEntityCount { get; set; }
    public long EstimatedSizeBytes { get; set; }
    public bool DisableConstraints { get; set; } = true;
    public bool DisableIndexes { get; set; } = true;
    public bool UseBulkMode { get; set; } = true;
    public string? OptimizationLevel { get; set; } // "Fast", "Balanced", "Safe"
}

public class RestorePreparationContext
{
    public string AdapterType { get; set; } = default!;
    public Dictionary<string, object> PreparationState { get; set; } = new();
    public DateTimeOffset PreparedAt { get; set; }
    public RestorePreparationOptions Options { get; set; } = default!;
}

// Adapter-specific implementations
public class SqlServerRestoreOptimizations
{
    public async Task<RestorePreparationContext> PrepareForRestoreAsync(RestorePreparationOptions options, CancellationToken ct)
    {
        var context = new RestorePreparationContext { AdapterType = "SqlServer" };

        if (options.DisableConstraints)
        {
            // Store constraint info and disable them
            var constraints = await GetForeignKeyConstraintsAsync(ct);
            context.PreparationState["constraints"] = constraints;
            await DisableForeignKeyConstraintsAsync(constraints, ct);
        }

        if (options.DisableIndexes)
        {
            // Store index info and drop non-clustered indexes
            var indexes = await GetNonClusteredIndexesAsync(ct);
            context.PreparationState["indexes"] = indexes;
            await DropNonClusteredIndexesAsync(indexes, ct);
        }

        if (options.UseBulkMode)
        {
            // Enable BULK INSERT optimizations
            await ExecuteAsync("ALTER DATABASE CURRENT SET RECOVERY BULK_LOGGED", ct);
            context.PreparationState["recovery_model_changed"] = true;
        }

        return context;
    }

    public async Task RestoreNormalOperationAsync(RestorePreparationContext context, CancellationToken ct)
    {
        // Restore constraints
        if (context.PreparationState.TryGetValue("constraints", out var constraintsObj))
        {
            var constraints = (List<ConstraintInfo>)constraintsObj;
            await RestoreForeignKeyConstraintsAsync(constraints, ct);
        }

        // Rebuild indexes
        if (context.PreparationState.TryGetValue("indexes", out var indexesObj))
        {
            var indexes = (List<IndexInfo>)indexesObj;
            await RebuildIndexesAsync(indexes, ct);
        }

        // Restore normal recovery model
        if (context.PreparationState.ContainsKey("recovery_model_changed"))
        {
            await ExecuteAsync("ALTER DATABASE CURRENT SET RECOVERY FULL", ct);
        }
    }
}

public class MongoRestoreOptimizations
{
    public async Task<RestorePreparationContext> PrepareForRestoreAsync(RestorePreparationOptions options, CancellationToken ct)
    {
        var context = new RestorePreparationContext { AdapterType = "MongoDB" };

        if (options.DisableIndexes)
        {
            // Store index definitions and drop them (except _id)
            var indexes = await GetIndexDefinitionsAsync(ct);
            context.PreparationState["indexes"] = indexes;
            await DropIndexesExceptIdAsync(ct);
        }

        if (options.UseBulkMode)
        {
            // Disable document validation
            await SetValidationLevelAsync("off", ct);
            context.PreparationState["validation_disabled"] = true;

            // Use unordered bulk operations
            context.PreparationState["use_unordered_bulk"] = true;
        }

        return context;
    }
}
```

---

## 2. Backup Storage Format

### 2.1 Simple JSON with ZIP Compression

**Core Principles:**

- Simple JSON format for maximum compatibility and debugging
- ZIP compression at archive level (not per-file)
- No encryption at this phase (focused on baseline functionality)
- Structured storage hierarchy for easy navigation

### 2.2 Backup Archive Structure

```
backup-{name}-{timestamp}.zip
├── manifest.json                     // BackupManifest metadata
├── entities/
│   ├── User.jsonl                   // JSON Lines format for streaming
│   ├── Order.jsonl
│   ├── Product.jsonl
│   └── AuditLog#archive.jsonl       // Set-specific data
├── verification/
│   ├── checksums.json               // File integrity verification
│   └── schema-snapshots.json        // Entity schema at backup time
└── metadata/
    ├── discovery-info.json          // Entity discovery metadata
    ├── provider-info.json           // Data provider configurations
    └── backup-stats.json            // Performance and size statistics
```

### 2.3 JSON Lines Entity Format

```jsonl
{"id":"user_123","name":"John Doe","email":"john@example.com","createdAt":"2025-01-23T10:30:00Z"}
{"id":"user_124","name":"Jane Smith","email":"jane@example.com","createdAt":"2025-01-23T11:15:00Z"}
{"id":"user_125","name":"Bob Wilson","email":"bob@example.com","createdAt":"2025-01-23T12:45:00Z"}
```

**Advantages:**

- Streaming-friendly (line-by-line processing)
- Memory efficient for large datasets
- Easy to parse and debug
- Standard format with wide tool support
- Compresses very well with ZIP

### 2.4 Backup Manifest Structure

```json
{
  "id": "backup_67890abc",
  "name": "production-daily-2025-01-23",
  "description": "Daily production backup",
  "version": "1.0",
  "createdAt": "2025-01-23T02:00:00Z",
  "completedAt": "2025-01-23T02:15:00Z",
  "duration": "00:15:23",
  "status": "completed",
  "tags": ["production", "daily", "automated"],
  "metadata": {
    "backup_reason": "scheduled",
    "triggered_by": "system",
    "koan_version": "2.1.0"
  },
  "entities": [
    {
      "entityType": "User",
      "keyType": "string",
      "set": "root",
      "itemCount": 15420,
      "sizeBytes": 2156789,
      "contentHash": "sha256:abc123...",
      "storageFile": "entities/User.jsonl",
      "provider": "postgres",
      "schemaSnapshot": {
        "properties": ["Id", "Name", "Email", "CreatedAt"],
        "types": ["string", "string", "string", "DateTimeOffset"]
      }
    }
  ],
  "verification": {
    "overallChecksum": "sha256:def456...",
    "totalSizeBytes": 45678901,
    "totalItemCount": 234567,
    "compressionRatio": 0.23
  },
  "discovery": {
    "discoveredAt": "2025-01-23T01:58:00Z",
    "totalEntityTypes": 12,
    "totalProviders": 2,
    "entitiesByProvider": {
      "postgres": 8,
      "mongo": 4
    }
  }
}
```

---

## 3. Enhanced Entity Discovery System

### 3.1 Startup Entity Discovery

```csharp
public interface IEntityDiscoveryService
{
    Task<EntityDiscoveryResult> DiscoverAllEntitiesAsync(CancellationToken ct = default);
    Task WarmupAllEntitiesAsync(CancellationToken ct = default);
    IEnumerable<EntityTypeInfo> GetDiscoveredEntities();
}

public class EntityDiscoveryService : IEntityDiscoveryService
{
    public async Task<EntityDiscoveryResult> DiscoverAllEntitiesAsync(CancellationToken ct = default)
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        var entityTypes = new List<EntityTypeInfo>();

        foreach (var assembly in assemblies)
        {
            try
            {
                var types = assembly.GetTypes()
                    .Where(t => !t.IsAbstract &&
                               !t.IsInterface &&
                               IsEntityType(t))
                    .ToList();

                foreach (var type in types)
                {
                    var keyType = GetEntityKeyType(type);
                    if (keyType != null)
                    {
                        entityTypes.Add(new EntityTypeInfo
                        {
                            EntityType = type,
                            KeyType = keyType,
                            Assembly = assembly.FullName,
                            Provider = ResolveProvider(type),
                            Sets = DiscoverEntitySets(type)
                        });
                    }
                }
            }
            catch { /* Skip problematic assemblies */ }
        }

        return new EntityDiscoveryResult { Entities = entityTypes };
    }

    public async Task WarmupAllEntitiesAsync(CancellationToken ct = default)
    {
        var discovered = await DiscoverAllEntitiesAsync(ct);

        // Pre-activate all discovered entities by accessing their configs
        foreach (var entity in discovered.Entities)
        {
            try
            {
                // Use reflection to call AggregateConfigs.Get<TEntity, TKey>()
                var getMethod = typeof(AggregateConfigs)
                    .GetMethod(nameof(AggregateConfigs.Get))
                    .MakeGenericMethod(entity.EntityType, entity.KeyType);

                getMethod.Invoke(null, new object[] { _serviceProvider });
                // This populates the AggregateConfigs.Cache with ALL entities
            }
            catch { /* Log but continue */ }
        }
    }

    private static bool IsEntityType(Type type) =>
        type.GetInterfaces().Any(i =>
            i.IsGenericType &&
            i.GetGenericTypeDefinition() == typeof(IEntity<>));
}

// Enhanced startup registration
public class EntityWarmupService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(1000, stoppingToken); // Let other services start

        _logger.LogInformation("Starting entity discovery and warmup...");
        await _discoveryService.WarmupAllEntitiesAsync(stoppingToken);
        _logger.LogInformation("Discovered and activated {Count} entity types",
            AggregateConfigs.GetAllRegisteredEntities().Count());

        // Now AggregateConfigs.Cache contains ALL entities, ready for backup
    }
}
```

---

## 4. Backup Operations Implementation

### 4.1 Streaming-Based Backup Service

```csharp
public interface IBackupService
{
    // Entity-specific backups
    Task<BackupManifest> BackupEntity<TEntity, TKey>(string backupName, BackupOptions options, CancellationToken ct = default)
        where TEntity : class, IEntity<TKey> where TKey : notnull;

    // All-entity backups
    Task<BackupManifest> BackupAllEntities(string backupName, GlobalBackupOptions options, CancellationToken ct = default);

    // Selective backups
    Task<BackupManifest> BackupSelected(string backupName, Func<EntityTypeInfo, bool> filter, GlobalBackupOptions options, CancellationToken ct = default);
}

public class StreamingBackupService : IBackupService
{
    public async Task<BackupManifest> BackupEntity<TEntity, TKey>(string backupName, BackupOptions options, CancellationToken ct = default)
        where TEntity : class, IEntity<TKey> where TKey : notnull
    {
        var entityInfo = new EntityBackupInfo
        {
            EntityType = typeof(TEntity).Name,
            KeyType = typeof(TKey).Name,
            Provider = AggregateConfigs.Get<TEntity, TKey>(_serviceProvider).Provider,
            Set = options.Set ?? "root"
        };

        // Create ZIP archive
        using var archive = new ZipArchive(await OpenBackupStream(backupName, options.StorageProfile), ZipArchiveMode.Create);

        // Stream entities to JSON Lines format
        var entityEntry = archive.CreateEntry($"entities/{typeof(TEntity).Name}.jsonl");
        using var entityStream = entityEntry.Open();
        using var writer = new StreamWriter(entityStream, Encoding.UTF8);

        var count = 0;
        var totalBytes = 0L;
        var hasher = SHA256.Create();

        // Use AllStream for memory-efficient streaming
        await foreach (var entity in Data<TEntity, TKey>.AllStream(batchSize: 1000, ct))
        {
            var json = JsonConvert.SerializeObject(entity, _jsonSettings);
            var line = json + "\n";
            var bytes = Encoding.UTF8.GetBytes(line);

            await writer.WriteLineAsync(json);
            hasher.TransformBlock(bytes, 0, bytes.Length, null, 0);

            count++;
            totalBytes += bytes.Length;

            if (count % 10000 == 0)
            {
                _logger.LogInformation("Backed up {Count} {EntityType} entities...", count, typeof(TEntity).Name);
            }
        }

        hasher.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        entityInfo.ContentHash = Convert.ToHexString(hasher.Hash!).ToLowerInvariant();
        entityInfo.ItemCount = count;
        entityInfo.SizeBytes = totalBytes;

        // Create and store manifest
        var manifest = new BackupManifest
        {
            Id = Guid.CreateVersion7().ToString(),
            Name = backupName,
            CreatedAt = DateTimeOffset.UtcNow,
            Entities = new List<EntityBackupInfo> { entityInfo }
        };

        var manifestEntry = archive.CreateEntry("manifest.json");
        using var manifestStream = manifestEntry.Open();
        await JsonSerializer.SerializeAsync(manifestStream, manifest, cancellationToken: ct);

        return manifest;
    }

    public async Task<BackupManifest> BackupAllEntities(string backupName, GlobalBackupOptions options, CancellationToken ct = default)
    {
        // Ensure all entities are discovered and activated
        await _discoveryService.WarmupAllEntitiesAsync(ct);

        var allEntities = AggregateConfigs.GetAllRegisteredEntities();
        var manifest = new BackupManifest
        {
            Id = Guid.CreateVersion7().ToString(),
            Name = backupName,
            CreatedAt = DateTimeOffset.UtcNow,
            Description = options.Description ?? $"Full backup of {allEntities.Count()} entity types"
        };

        // Create ZIP archive
        using var archive = new ZipArchive(await OpenBackupStream(backupName, options.StorageProfile), ZipArchiveMode.Create);

        // Parallel entity backup with controlled concurrency
        var semaphore = new SemaphoreSlim(options.MaxConcurrency ?? Environment.ProcessorCount);
        var entityBackupTasks = allEntities.Select(async entityInfo =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                return await BackupEntityByReflection(entityInfo, archive, options, ct);
            }
            finally
            {
                semaphore.Release();
            }
        });

        var results = await Task.WhenAll(entityBackupTasks);
        manifest.Entities = results.Where(r => r != null).ToList();

        // Store manifest and verification data
        await StoreManifestAndVerification(archive, manifest, ct);

        return manifest;
    }

    private async Task<EntityBackupInfo?> BackupEntityByReflection(EntityTypeInfo entityInfo, ZipArchive archive, GlobalBackupOptions options, CancellationToken ct)
    {
        try
        {
            // Use reflection to call AllStream for unknown entity types
            var dataType = typeof(Data<,>).MakeGenericType(entityInfo.EntityType, entityInfo.KeyType);
            var allStreamMethod = dataType.GetMethod(nameof(Data<object, object>.AllStream), new[] { typeof(int?), typeof(CancellationToken) });

            var asyncEnumerable = allStreamMethod.Invoke(null, new object[] { 1000, ct });
            var enumeratorMethod = asyncEnumerable.GetType().GetMethod("GetAsyncEnumerator");
            var enumerator = enumeratorMethod.Invoke(asyncEnumerable, new object[] { ct });

            // Stream to JSON Lines
            var entityEntry = archive.CreateEntry($"entities/{entityInfo.EntityType.Name}.jsonl");
            using var entityStream = entityEntry.Open();
            using var writer = new StreamWriter(entityStream, Encoding.UTF8);

            var count = 0;
            var hasher = SHA256.Create();

            // Iterate through async enumerable via reflection
            var moveNextMethod = enumerator.GetType().GetMethod("MoveNextAsync");
            var currentProperty = enumerator.GetType().GetProperty("Current");

            while (true)
            {
                var moveNextTask = (ValueTask<bool>)moveNextMethod.Invoke(enumerator, null);
                if (!await moveNextTask) break;

                var entity = currentProperty.GetValue(enumerator);
                var json = JsonConvert.SerializeObject(entity, _jsonSettings);
                var bytes = Encoding.UTF8.GetBytes(json + "\n");

                await writer.WriteLineAsync(json);
                hasher.TransformBlock(bytes, 0, bytes.Length, null, 0);
                count++;
            }

            hasher.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

            return new EntityBackupInfo
            {
                EntityType = entityInfo.EntityType.Name,
                KeyType = entityInfo.KeyType.Name,
                Provider = entityInfo.Provider,
                ItemCount = count,
                ContentHash = Convert.ToHexString(hasher.Hash!).ToLowerInvariant(),
                StorageFile = $"entities/{entityInfo.EntityType.Name}.jsonl"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to backup entity type {EntityType}", entityInfo.EntityType.Name);
            return null;
        }
    }
}
```

### 4.2 Restore Operations with Adapter Optimization

```csharp
public interface IRestoreService
{
    Task RestoreEntity<TEntity, TKey>(string backupName, RestoreOptions options, CancellationToken ct = default)
        where TEntity : class, IEntity<TKey> where TKey : notnull;

    Task RestoreAllEntities(string backupName, GlobalRestoreOptions options, CancellationToken ct = default);

    Task<RestoreViabilityReport> TestRestoreViability(string backupName, CancellationToken ct = default);
}

public class OptimizedRestoreService : IRestoreService
{
    public async Task RestoreEntity<TEntity, TKey>(string backupName, RestoreOptions options, CancellationToken ct = default)
        where TEntity : class, IEntity<TKey> where TKey : notnull
    {
        var repository = AggregateConfigs.Get<TEntity, TKey>(_serviceProvider).Repository;
        RestorePreparationContext? preparationContext = null;

        try
        {
            // Prepare adapter for bulk operations if supported
            if (repository is IRestoreOptimizedRepository<TEntity, TKey> optimizedRepo)
            {
                var prepOptions = new RestorePreparationOptions
                {
                    EstimatedEntityCount = await EstimateEntityCount<TEntity>(backupName, ct),
                    DisableConstraints = options.DisableConstraints,
                    DisableIndexes = options.DisableIndexes,
                    UseBulkMode = options.UseBulkMode
                };

                preparationContext = await optimizedRepo.PrepareForRestoreAsync(prepOptions, ct);
                _logger.LogInformation("Prepared {EntityType} for optimized restore", typeof(TEntity).Name);
            }

            // Load and restore entities in batches
            var backupStream = await OpenBackupForReading(backupName, options.StorageProfile, ct);
            using var archive = new ZipArchive(backupStream, ZipArchiveMode.Read);

            var entityEntry = archive.GetEntry($"entities/{typeof(TEntity).Name}.jsonl");
            if (entityEntry == null)
                throw new InvalidOperationException($"No backup data found for {typeof(TEntity).Name}");

            using var entityStream = entityEntry.Open();
            using var reader = new StreamReader(entityStream);

            var batch = new List<TEntity>();
            var batchSize = options.BatchSize ?? 1000;
            var totalRestored = 0;

            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                var entity = JsonConvert.DeserializeObject<TEntity>(line, _jsonSettings);
                if (entity != null)
                {
                    batch.Add(entity);

                    if (batch.Count >= batchSize)
                    {
                        await Data<TEntity, TKey>.UpsertManyAsync(batch, ct);
                        totalRestored += batch.Count;
                        batch.Clear();

                        _logger.LogInformation("Restored {Count} {EntityType} entities...", totalRestored, typeof(TEntity).Name);
                    }
                }
            }

            // Process final batch
            if (batch.Count > 0)
            {
                await Data<TEntity, TKey>.UpsertManyAsync(batch, ct);
                totalRestored += batch.Count;
            }

            _logger.LogInformation("Successfully restored {Count} {EntityType} entities", totalRestored, typeof(TEntity).Name);
        }
        finally
        {
            // Restore normal adapter operation
            if (preparationContext != null && repository is IRestoreOptimizedRepository<TEntity, TKey> optimizedRepo)
            {
                await optimizedRepo.RestoreNormalOperationAsync(preparationContext, ct);
                _logger.LogInformation("Restored normal operation for {EntityType}", typeof(TEntity).Name);
            }
        }
    }
}
```

---

## 5. Developer Experience APIs

### 5.1 Ultra-Simple Static Methods

```csharp
// Entity-level backup/restore
public static class EntityBackupExtensions
{
    public static Task<BackupManifest> BackupTo<TEntity, TKey>(this Entity<TEntity, TKey> entity, string backupName, string? description = null, CancellationToken ct = default)
        where TEntity : class, IEntity<TKey> where TKey : notnull
        => App.GetService<IBackupService>().BackupEntity<TEntity, TKey>(backupName, new BackupOptions { Description = description }, ct);

    public static Task RestoreFrom<TEntity, TKey>(this Entity<TEntity, TKey> entity, string backupName, bool replaceExisting = false, CancellationToken ct = default)
        where TEntity : class, IEntity<TKey> where TKey : notnull
        => App.GetService<IRestoreService>().RestoreEntity<TEntity, TKey>(backupName, new RestoreOptions { ReplaceExisting = replaceExisting }, ct);
}

// Global backup operations
public static class GlobalBackup
{
    public static Task<BackupManifest> BackupAll(string backupName, string? description = null)
        => App.GetService<IBackupService>().BackupAllEntities(backupName, new GlobalBackupOptions { Description = description });

    public static Task<BackupManifest> BackupByProvider(string backupName, params string[] providers)
        => App.GetService<IBackupService>().BackupSelected(backupName, entity => providers.Contains(entity.Provider), new GlobalBackupOptions());

    public static Task RestoreAll(string backupName, bool replaceExisting = false)
        => App.GetService<IRestoreService>().RestoreAllEntities(backupName, new GlobalRestoreOptions { ReplaceExisting = replaceExisting });
}

// Usage examples
await User.BackupTo("daily-users", "Daily user backup");
await Order.RestoreFrom("pre-migration", replaceExisting: true);
await GlobalBackup.BackupAll("system-backup-v1");
await GlobalBackup.BackupByProvider("mongo-only", "mongo", "postgres");
```

### 5.2 Fluent Builder API

```csharp
public interface IBackupBuilder<TEntity, TKey>
{
    IBackupBuilder<TEntity, TKey> To(string backupName);
    IBackupBuilder<TEntity, TKey> WithDescription(string description);
    IBackupBuilder<TEntity, TKey> WithTags(params string[] tags);
    IBackupBuilder<TEntity, TKey> FromSet(string setName);
    IBackupBuilder<TEntity, TKey> ToStorageProfile(string profile);
    IBackupBuilder<TEntity, TKey> WithCompression(CompressionLevel level = CompressionLevel.Optimal);
    IBackupBuilder<TEntity, TKey> WithVerification(bool enabled = true);
    Task<BackupManifest> ExecuteAsync(CancellationToken ct = default);
}

// Usage
await User.Backup()
    .To("filtered-users")
    .WithDescription("Active users only")
    .WithTags("filtered", "users")
    .ToStorageProfile("cloud-backup")
    .WithCompression(CompressionLevel.Optimal)
    .ExecuteAsync();
```

### 5.3 HTTP REST API

```http
# Entity-specific operations
POST /api/users/backup
GET  /api/users/backups
POST /api/users/restore/{backupName}

# Global operations
POST /api/backup/all
POST /api/backup/selective
GET  /api/backup/manifests
POST /api/backup/restore/{backupName}

# Management
GET  /api/backup/status
POST /api/backup/verify/{backupName}
```

---

## 6. Backup Discovery & Listing System

### 6.1 Multi-Storage Discovery

```csharp
public interface IBackupDiscoveryService
{
    Task<BackupCatalog> DiscoverAllBackupsAsync(DiscoveryOptions? options = null, CancellationToken ct = default);
    Task<BackupCatalog> QueryBackupsAsync(BackupQuery query, CancellationToken ct = default);
    Task<BackupInfo?> GetBackupAsync(string backupId, CancellationToken ct = default);
}

// Static query API
public static class BackupCatalog
{
    public static IBackupQueryBuilder Query() => new BackupQueryBuilder(App.GetService<IBackupDiscoveryService>());

    // Shortcuts
    public static Task<BackupInfo[]> All() => Query().ToArrayAsync();
    public static Task<BackupInfo[]> Recent(int days = 7) => Query().FromLast(TimeSpan.FromDays(days)).ToArrayAsync();
    public static Task<BackupInfo?> Latest() => Query().OrderByCreatedDate().Take(1).FirstOrNullAsync();
}

// Usage examples
var recentBackups = await BackupCatalog.Recent(30);
var productionBackups = await BackupCatalog.Query().WithTags("production").ToArrayAsync();
var userBackups = await BackupCatalog.Query().ContainingEntity<User>().ToArrayAsync();
```

### 6.2 Rich Querying

```csharp
public interface IBackupQueryBuilder
{
    // Temporal filters
    IBackupQueryBuilder FromDate(DateTimeOffset from);
    IBackupQueryBuilder FromLast(TimeSpan period);
    IBackupQueryBuilder InDateRange(DateTimeOffset from, DateTimeOffset to);

    // Content filters
    IBackupQueryBuilder WithTags(params string[] tags);
    IBackupQueryBuilder ContainingEntity<TEntity>();
    IBackupQueryBuilder InStorageProfile(params string[] profiles);

    // Status filters
    IBackupQueryBuilder Healthy();
    IBackupQueryBuilder RequiringAttention();

    // Text search
    IBackupQueryBuilder Search(string term);

    // Execution
    Task<BackupInfo[]> ToArrayAsync(CancellationToken ct = default);
    Task<BackupInfo?> FirstOrNullAsync(CancellationToken ct = default);
    IAsyncEnumerable<BackupInfo> AsAsyncEnumerable(CancellationToken ct = default);
}
```

---

## 7. Configuration & Setup

### 7.1 Service Registration

```csharp
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKoanBackupRestore(this IServiceCollection services,
        Action<BackupRestoreOptions>? configure = null)
    {
        var options = new BackupRestoreOptions();
        configure?.Invoke(options);

        // Core services
        services.AddSingleton<IEntityDiscoveryService, EntityDiscoveryService>();
        services.AddSingleton<IBackupService, StreamingBackupService>();
        services.AddSingleton<IRestoreService, OptimizedRestoreService>();
        services.AddSingleton<IBackupDiscoveryService, BackupDiscoveryService>();

        // Background services
        if (options.WarmupEntitiesOnStartup)
        {
            services.AddHostedService<EntityWarmupService>();
        }

        if (options.EnableBackgroundDiscovery)
        {
            services.AddHostedService<BackgroundDiscoveryService>();
        }

        return services;
    }
}

// Usage
services.AddKoan()
    .WithData()
    .WithStorage()
    .WithBackupRestore(options =>
    {
        options.DefaultStorageProfile = "backup-storage";
        options.WarmupEntitiesOnStartup = true;
        options.EnableBackgroundDiscovery = true;
        options.RetentionDays = 30;
        options.CompressionLevel = CompressionLevel.Optimal;
    });
```

### 7.2 Configuration Example

```json
{
  "Koan": {
    "Storage": {
      "DefaultProfile": "primary",
      "Profiles": {
        "primary": {
          "Provider": "postgres",
          "Container": "main_db"
        },
        "backup-storage": {
          "Provider": "s3",
          "Container": "company-backups"
        }
      }
    },
    "BackupRestore": {
      "DefaultStorageProfile": "backup-storage",
      "WarmupEntitiesOnStartup": true,
      "EnableBackgroundDiscovery": true,
      "RetentionDays": 90,
      "CompressionLevel": "Optimal",
      "MaxConcurrentBackups": 4,
      "BatchSize": 1000,
      "EnableAdapterOptimization": true
    }
  }
}
```

---

## 8. Implementation Roadmap

### Phase 1: Foundation (4-6 weeks)

**Goal**: Basic backup/restore with streaming and entity discovery

**Week 1-2: Core Infrastructure**

- [ ] Enhanced AggregateConfigs with discovery methods
- [ ] EntityDiscoveryService with reflection-based discovery
- [ ] Entity warmup background service
- [ ] Basic BackupManifest and EntityBackupInfo models

**Week 3-4: Streaming Backup**

- [ ] StreamingBackupService with AllStream integration
- [ ] JSON Lines format with ZIP compression
- [ ] Single entity backup/restore operations
- [ ] Content hashing and verification

**Week 5-6: All-Entity Operations**

- [ ] Global backup with parallel entity processing
- [ ] Reflection-based entity backup via AllStream
- [ ] Basic restore operations using UpsertMany
- [ ] Configuration system and service registration

**Success Criteria:**

- `await User.BackupTo("backup-name")` works
- `await GlobalBackup.BackupAll("system-backup")` works
- JSON Lines format with ZIP compression
- Cross-provider backup (MongoDB → S3)

### Phase 2: Optimization & Discovery (3-4 weeks)

**Goal**: Adapter optimizations and backup discovery

**Week 7-8: Adapter Optimization**

- [ ] IRestoreOptimizedRepository interface
- [ ] SQL Server optimization implementation
- [ ] MongoDB optimization implementation
- [ ] RestorePreparationContext system

**Week 9-10: Backup Discovery**

- [ ] BackupDiscoveryService with multi-storage scanning
- [ ] BackupCatalog with static query methods
- [ ] BackupQueryBuilder with fluent API
- [ ] Background discovery and caching

**Success Criteria:**

- 10x faster restores with adapter optimization
- `await BackupCatalog.Recent(30)` returns all backups
- Rich querying with complex filters
- Background discovery keeps catalog current

### Phase 3: Advanced Features (2-3 weeks)

**Goal**: Enterprise features and production readiness

**Week 11-12: Advanced Features**

- [ ] Backup health monitoring and validation
- [ ] HTTP REST API endpoints
- [ ] Real-time progress reporting
- [ ] Automated retention and cleanup

**Week 13: Production Hardening**

- [ ] Performance benchmarking and optimization
- [ ] Comprehensive error handling
- [ ] Monitoring and metrics integration
- [ ] Documentation and examples

**Success Criteria:**

- Handle 10GB+ datasets efficiently
- Enterprise-grade reliability and monitoring
- Comprehensive documentation
- Performance meets targets

---

## 9. Performance Targets

| Scenario                 | Target Performance           | Scaling Behavior           |
| ------------------------ | ---------------------------- | -------------------------- |
| **Entity Discovery**     | < 2 seconds for 100 entities | O(n) with assembly caching |
| **Single Entity Backup** | 1GB/minute streaming         | O(n) memory usage          |
| **All-Entity Backup**    | 500MB/minute compressed      | Parallel across entities   |
| **Adapter Optimization** | 10x restore speedup          | Provider-specific gains    |
| **Backup Discovery**     | < 500ms for 1000 backups     | O(log n) with indexing     |
| **ZIP Compression**      | 60-80% size reduction        | Varies by data type        |

---

## 10. Testing Strategy

### 10.1 Unit Tests

- Entity discovery across different assembly configurations
- Streaming backup/restore with various data sizes
- Adapter optimization preparation/restoration
- JSON Lines serialization/deserialization
- ZIP archive creation/extraction

### 10.2 Integration Tests

- End-to-end backup/restore workflows
- Cross-provider scenarios (MongoDB → PostgreSQL)
- Multi-storage profile discovery
- Large dataset streaming (1M+ entities)
- Concurrent backup operations

### 10.3 Performance Tests

- Backup/restore speed benchmarks
- Memory usage profiling during streaming
- Compression ratio analysis
- Adapter optimization effectiveness
- Discovery performance with large entity counts

---

## 11. Security Considerations

### 11.1 Current Scope (No Encryption)

- **Focus**: Baseline functionality with simple JSON format
- **Storage Security**: Relies on underlying storage security (S3 encryption, etc.)
- **Access Control**: Uses existing Koan.Storage profile-based access
- **Transport**: HTTPS for cloud storage, secure connections for databases

### 11.2 Future Encryption Strategy

- **Archive-Level Encryption**: Encrypt entire ZIP before storage
- **Key Management**: Integration with Koan.Secrets for key handling
- **Compliance**: Support for various encryption standards (AES-256, etc.)
- **Performance**: Streaming encryption to maintain memory efficiency

---

## 12. Success Metrics

### 12.1 Developer Experience

- **Time to First Backup**: < 5 minutes from project setup
- **API Discoverability**: 95%+ of features discoverable via IntelliSense
- **Learning Curve**: Common scenarios completable within 30 minutes
- **Framework Consistency**: APIs feel native to existing Koan patterns

### 12.2 Technical Performance

- **Backup Speed**: 1GB/minute for JSON data, 500MB/minute compressed
- **Memory Usage**: < 100MB baseline + streaming overhead
- **Discovery Speed**: < 2 seconds for 100 entity types
- **Restoration Speed**: 80% of backup speed, 10x with optimization

### 12.3 Operational Excellence

- **Reliability**: 99.9% backup success rate for properly configured systems
- **Data Integrity**: 100% verification success rate
- **Cross-Provider Support**: Works with all Koan.Data providers
- **Monitoring Coverage**: Full observability into backup operations

---

## Conclusion

This comprehensive backup/restore system transforms data protection from an afterthought into a **first-class citizen** of the Koan Framework ecosystem. By leveraging existing infrastructure (Data<>, Storage, Aggregates) and providing exceptional developer experience, it delivers enterprise-grade capabilities with zero-configuration simplicity.

The design's key strengths:

- **Framework-Native**: Builds on proven Koan patterns and infrastructure
- **Streaming-First**: Memory-efficient for any dataset size
- **Provider-Agnostic**: Works across all storage backends
- **Optimization-Aware**: Adapters can optimize themselves for bulk operations
- **Progressive DX**: From one-liners to sophisticated enterprise scenarios

From `await GlobalBackup.BackupAll("production-v2.1.0")` to complex cross-provider scenarios, this system provides the foundation for reliable, efficient, and delightful data protection in Koan Framework applications.

---

**Next Steps**: Review and approval of this specification, followed by Phase 1 implementation beginning with the enhanced AggregateConfigs and EntityDiscoveryService foundations.
