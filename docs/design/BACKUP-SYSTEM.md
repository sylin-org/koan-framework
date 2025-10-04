# Koan Framework Backup System - Design Specification

**Version**: 1.0
**Status**: Design Specification
**Date**: 2025-10-02

## Overview

The Koan Backup system provides attribute-driven backup and restore capabilities for entities and vectors with explicit opt-in and startup validation. It follows the framework's core principles: provider transparency, minimal scaffolding, and safe defaults that prevent silent data loss.

## Core Principles

### 1. Explicit Opt-In with Discovery Support
- Entities declare backup participation via `[EntityBackup]` attribute
- Assemblies can opt-in all entities with `[assembly: EntityBackupScope]`
- Startup inventory validates coverage and warns about missing declarations
- Prevents silent data loss from unbounded auto-discovery

### 2. Policy-Driven Configuration
- Per-entity policies: `Encrypt = true`, `IncludeSchema = false`
- Assembly-level defaults: `EncryptByDefault`, scope inheritance
- Runtime validation ensures policy compliance
- Manifest captures applied policies for audit trails

### 3. Manifest Integrity First
- Failed backups marked explicitly with `Status = Failed`
- `EntityBackupInfo.ErrorMessage` captures failure details
- Restore operations refuse to proceed with corrupted manifests
- No silent failures or empty dataset acceptance

### 4. Thin Adapters
- Adapters remain dumb storage implementations
- Backup orchestrates using existing `Data<T>` and `Vector<T>` APIs
- Optional constraint management via `IConstraintManager` interface

### 5. Provider Transparency
- Export from Weaviate, restore to ElasticSearch
- Vector embeddings cached in portable format
- Zero AI regeneration cost when switching providers

### 6. Pragmatic HTTP Semantics
- `GET /backup/create` for simplicity over REST convention
- Administrative operations, not REST resources
- Clear intent over protocol purity

---

## Architecture

```
┌─────────────────────────────────────┐
│  Koan.Data.Backup (Orchestrator)    │
│  - Discovery                         │
│  - Serialization                     │
│  - File management                   │
└──────────────┬──────────────────────┘
               │ uses existing APIs
               ↓
┌─────────────────────────────────────┐
│  Data<T>, Vector<T>                 │
│  - AllStream()                       │
│  - Save()                            │
│  - SaveWithVector()                  │
└──────────────┬──────────────────────┘
               │ delegates to
               ↓
┌─────────────────────────────────────┐
│  Adapters (Thin)                    │
│  - Storage operations                │
│  - Optional: IConstraintManager      │
└─────────────────────────────────────┘
```

### Separation of Concerns

| Layer | Responsibility | Does NOT |
|-------|----------------|----------|
| **Koan.Data.Backup** | Orchestration, discovery, file I/O | Know adapter internals |
| **Data<T>/Vector<T>** | Entity operations | Know about backups |
| **Adapters** | Storage execution | Know about backups |

---

## API Design

### C# API

```csharp
// Single entity backup
await Backup<Media>.Create(new BackupOptions
{
    Include = new[] { "data", "vectors" },
    Path = ".koan/backups"
});

// Single entity restore
await Backup<Media>.Restore(new RestoreOptions
{
    Timestamp = "2025-10-02T15-30-45Z",
    OverwriteExisting = false
});

// All entities (auto-discovered)
await Backup.Create(new BackupOptions
{
    Include = new[] { "data", "vectors" }
});

// Specific entities
await Backup.CreateFor(
    new[] { typeof(Media), typeof(User) },
    new BackupOptions { Include = new[] { "data" } }
);

// Discovery
var capabilities = await Backup.GetCapabilities();
```

### REST API

```http
# Discovery
GET /backup/capabilities
# Returns available sources and entities

# Creation
GET /backup/create
# Full backup with defaults

POST /backup/create
{
  "entities": ["Media", "User"],
  "include": ["data", "vectors"]
}

# Entity-specific
GET /backup/Media/create

# Restoration
POST /backup/Media/2025-10-02T15-30-45Z/restore
{
  "overwriteExisting": false
}

# Information
GET /backup/Media
# List all backups

GET /backup/Media/2025-10-02T15-30-45Z/info
# Get backup details

# Management
DELETE /backup/Media/2025-10-02T15-30-45Z
# Delete backup

GET /backup/Media/2025-10-02T15-30-45Z/download
# Download backup archive
```

---

## File Structure

```
.koan/
└── backups/
    ├── S5.Recs.Models.Media/
    │   ├── 2025-10-02T15-30-45Z/
    │   │   ├── data.json              # Entity data
    │   │   ├── vectors/               # Vector embeddings (cache format)
    │   │   │   └── default/
    │   │   │       ├── {hash}.json
    │   │   │       └── ...
    │   │   └── metadata.json          # Backup metadata
    │   ├── 2025-10-03T09-15-30Z/
    │   └── latest -> 2025-10-03T09-15-30Z/
    ├── S5.Recs.Models.User/
    │   └── 2025-10-02T15-30-45Z/
    └── manifest.json                  # Global backup registry
```

### Path Configuration

**Development:**
- Default: `.koan/backups/`
- Project-local for easy Git exclusion

**Production:**
- Default: `/var/koan/backups/`
- FHS-compliant for Linux deployments

**Override:**
```json
{
  "Koan": {
    "Paths": {
      "Root": ".koan",
      "Backups": "backups"
    }
  }
}
```

---

## Entity Opt-In and Policy Configuration

### Entity-Level Opt-In

```csharp
using Koan.Data.Backup.Attributes;

// Basic opt-in
[EntityBackup]
public class Media : Entity<Media>
{
    public string Title { get; set; } = "";
    public string ContentUrl { get; set; } = "";
}

// With encryption for PII
[EntityBackup(Encrypt = true)]
public class User : Entity<User>
{
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
}

// Exclude schema to reduce backup size
[EntityBackup(IncludeSchema = false)]
public class LogEntry : Entity<LogEntry>
{
    public string Message { get; set; } = "";
    public DateTimeOffset Timestamp { get; set; }
}

// Explicit opt-out
[EntityBackup(Enabled = false, Reason = "Derived view, rebuild from source")]
public class SearchIndex : Entity<SearchIndex>
{
    public string IndexData { get; set; } = "";
}
```

### Assembly-Level Scope

```csharp
using Koan.Data.Backup.Attributes;

// Opt-in all entities in this assembly
[assembly: EntityBackupScope(Mode = BackupScope.All)]

// With default encryption
[assembly: EntityBackupScope(Mode = BackupScope.All, EncryptByDefault = true)]

namespace MyApp.Models
{
    // Automatically included (inherits assembly scope)
    public class Media : Entity<Media> { }

    // Override assembly default
    [EntityBackup(Encrypt = false)]
    public class PublicContent : Entity<PublicContent> { }
}

// ---

// Require explicit decoration (strict mode)
[assembly: EntityBackupScope(Mode = BackupScope.None)]

namespace MyApp.SecureModels
{
    // Must explicitly opt-in
    [EntityBackup(Encrypt = true)]
    public class SensitiveData : Entity<SensitiveData> { }

    // Will generate startup warning (not backed up)
    public class UnmarkedEntity : Entity<UnmarkedEntity> { }
}
```

### Startup Inventory and Validation

```csharp
public class EntityDiscoveryService
{
    public BackupInventory BuildInventory(IServiceProvider services)
    {
        var inventory = new BackupInventory();

        // Discover all Entity<> types via AggregateConfigs
        var entityTypes = AggregateConfigs.GetAllRegisteredTypes();

        foreach (var entityType in entityTypes)
        {
            // Apply assembly scope
            var assemblyScope = GetAssemblyScope(entityType.Assembly);

            // Check for entity-level attribute
            var entityAttr = entityType.GetCustomAttribute<EntityBackupAttribute>();

            // Compute effective policy
            var policy = ResolvePolicy(assemblyScope, entityAttr);

            if (policy.IsIncluded)
            {
                inventory.IncludedEntities.Add(new EntityBackupPolicy
                {
                    EntityType = entityType,
                    Encrypt = policy.Encrypt,
                    IncludeSchema = policy.IncludeSchema,
                    Source = policy.Source // Assembly vs Entity attribute
                });
            }
            else if (policy.ShouldWarn)
            {
                inventory.Warnings.Add($"Entity {entityType.Name} has no backup coverage (assembly scope: {assemblyScope?.Mode})");
            }
        }

        return inventory;
    }
}
```

**Startup Output Example:**

```
[INFO] Koan:backup inventory validation
[INFO] Koan:backup   included: 12 entities
[INFO] Koan:backup     Media → encrypt=false, schema=true (via assembly scope)
[INFO] Koan:backup     User → encrypt=true, schema=true (via [EntityBackup])
[INFO] Koan:backup     LogEntry → encrypt=false, schema=false (via [EntityBackup])
[WARN] Koan:backup   uncovered: 2 entities
[WARN] Koan:backup     UnmarkedEntity → no backup coverage (assembly scope: None)
[INFO] Koan:backup   excluded: 1 entity (explicit opt-out)
[INFO] Koan:backup     SearchIndex → reason: "Derived view, rebuild from source"
```

---

## Discovery Mechanism

### Capability Detection

```csharp
public class BackupService
{
    private readonly IServiceProvider _services;
    private readonly IVectorService? _vectorService;
    private readonly BackupInventory _inventory;

    public BackupCapabilities GetCapabilities()
    {
        var capabilities = new BackupCapabilities();

        // Always have "data" if entities exist
        capabilities.AvailableSources.Add("data");

        // Check if vector service is registered
        if (_vectorService != null)
        {
            capabilities.AvailableSources.Add("vectors");
        }

        // Use inventory to get included entities only
        foreach (var policy in _inventory.IncludedEntities)
        {
            var entityType = policy.EntityType;
            var sources = new List<string> { "data" };

            // Check if entity has vectors
            if (_vectorService?.TryGetRepository(entityType) != null)
            {
                sources.Add("vectors");
            }

            capabilities.Entities[entityType.Name] = new EntityBackupInfo
            {
                FullTypeName = entityType.FullName!,
                AvailableSources = sources,
                Encrypt = policy.Encrypt,
                IncludeSchema = policy.IncludeSchema
            };
        }

        return capabilities;
    }
}
```

### Example Response

```json
GET /backup/capabilities

{
  "availableSources": ["data", "vectors"],
  "entities": {
    "Media": {
      "fullTypeName": "S5.Recs.Models.Media",
      "availableSources": ["data", "vectors"],
      "encrypt": false,
      "includeSchema": true
    },
    "User": {
      "fullTypeName": "S5.Recs.Models.User",
      "availableSources": ["data"],
      "encrypt": true,
      "includeSchema": true
    }
  },
  "warnings": [
    "Entity UnmarkedEntity has no backup coverage"
  ]
}
```

---

## Implementation Details

### Entity Export

```csharp
public async Task<BackupResult> ExportEntities<T, TKey>()
    where T : class
{
    var entityTypeName = typeof(T).FullName!;
    var targetPath = Path.Combine(
        ".koan/backups",
        entityTypeName,
        Timestamp(),
        "data.json"
    );

    Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

    await using var file = File.Create(targetPath);
    await using var writer = new Utf8JsonWriter(file);

    writer.WriteStartArray();

    var count = 0;
    await foreach (var entity in Data<T, TKey>.AllStream())
    {
        JsonSerializer.Serialize(writer, entity);
        count++;
    }

    writer.WriteEndArray();
    await writer.FlushAsync();

    return new BackupResult { EntityCount = count };
}
```

### Entity Import

```csharp
public async Task<BackupResult> ImportEntities<T, TKey>(string backupPath)
    where T : class
{
    var dataPath = Path.Combine(backupPath, "data.json");

    // Optional: Disable constraints if supported
    var constraintMgr = TryGetConstraintManager<T>();
    await constraintMgr?.DisableConstraintsAsync()!;

    try
    {
        await using var file = File.OpenRead(dataPath);
        var entities = JsonSerializer.DeserializeAsyncEnumerable<T>(file);

        var count = 0;
        await foreach (var entity in entities)
        {
            await Data<T, TKey>.Save(entity);
            count++;
        }

        return new BackupResult { EntityCount = count };
    }
    finally
    {
        await constraintMgr?.EnableConstraintsAsync()!;
    }
}
```

### Vector Export

```csharp
public async Task<BackupResult> ExportVectors<T, TKey>()
    where T : class
{
    var vectorRepo = _vectorService?.TryGetRepository<T, TKey>();
    if (vectorRepo == null) return new BackupResult();

    var cachePath = Path.Combine(
        ".koan/backups",
        typeof(T).FullName!,
        Timestamp(),
        "vectors"
    );

    var count = 0;
    await foreach (var batch in vectorRepo.ExportAllAsync())
    {
        // Load entity to build embedding text for content hash
        var entity = await Data<T, TKey>.Get(batch.Id);
        var embeddingText = BuildEmbeddingText(entity);
        var hash = ComputeHash(embeddingText);

        // Write to cache format for portability
        await WriteVectorToCache(cachePath, hash, batch.Embedding);
        count++;
    }

    return new BackupResult { VectorCount = count };
}
```

### Vector Import

```csharp
public async Task<BackupResult> ImportVectors<T, TKey>(string backupPath)
    where T : class
{
    var cachePath = Path.Combine(backupPath, "vectors");

    var count = 0;
    await foreach (var entity in Data<T, TKey>.AllStream())
    {
        var embeddingText = BuildEmbeddingText(entity);
        var hash = ComputeHash(embeddingText);

        // Read from cache
        var embedding = await ReadVectorFromCache(cachePath, hash);
        if (embedding == null) continue;

        // Save to current vector provider (may be different!)
        await Data<T, TKey>.SaveWithVector(entity, embedding, null);
        count++;
    }

    return new BackupResult { VectorCount = count };
}
```

---

## Optional: Constraint Management

### Interface

```csharp
// In Koan.Data.Abstractions
public interface IConstraintManager
{
    Task DisableConstraintsAsync(CancellationToken ct = default);
    Task EnableConstraintsAsync(CancellationToken ct = default);
}
```

### Adapter Implementation (Optional)

```csharp
// MongoDB supports it
public class MongoDataRepository<T, TKey>
    : IDataRepository<T, TKey>, IConstraintManager
{
    public async Task DisableConstraintsAsync(CancellationToken ct)
    {
        await _collection.Indexes.DropAllAsync(ct);
    }

    public async Task EnableConstraintsAsync(CancellationToken ct)
    {
        await EnsureIndexesAsync(ct);
    }
}

// SQLite doesn't need it
public class SqliteDataRepository<T, TKey> : IDataRepository<T, TKey>
{
    // No IConstraintManager implementation
}
```

### Usage in Backup

```csharp
private IConstraintManager? TryGetConstraintManager<T>()
{
    var repository = GetRepository<T>();
    return repository as IConstraintManager;
}
```

---

## Configuration

### Options Classes

```csharp
public class BackupOptions
{
    /// <summary>
    /// What to include. Default: all available sources.
    /// </summary>
    public string[]? Include { get; set; }

    /// <summary>
    /// Specific entities to backup. Default: all.
    /// </summary>
    public string[]? Entities { get; set; }

    /// <summary>
    /// Base path. Default: .koan/backups
    /// </summary>
    public string? Path { get; set; }

    /// <summary>
    /// Serialization format. Default: Json
    /// </summary>
    public BackupFormat Format { get; set; } = BackupFormat.Json;

    /// <summary>
    /// Compress backup. Default: true
    /// </summary>
    public bool Compress { get; set; } = true;
}

public class RestoreOptions
{
    /// <summary>
    /// Timestamp of backup to restore.
    /// </summary>
    public string? Timestamp { get; set; }

    /// <summary>
    /// Overwrite existing entities. Default: false
    /// </summary>
    public bool OverwriteExisting { get; set; }

    /// <summary>
    /// Restore vectors. Default: true if available
    /// </summary>
    public bool RestoreVectors { get; set; } = true;
}

public class BackupResult
{
    public string Timestamp { get; set; } = "";
    public int EntityCount { get; set; }
    public int VectorCount { get; set; }
    public TimeSpan Duration { get; set; }
    public long SizeBytes { get; set; }
}
```

### Application Configuration

```json
{
  "Koan": {
    "Paths": {
      "Root": ".koan",
      "Backups": "backups"
    },
    "Backup": {
      "DefaultFormat": "Json",
      "DefaultCompress": true,
      "RetentionDays": 30,
      "MaxBackupsPerEntity": 10
    }
  }
}
```

---

## Usage Examples

### Development Workflow

```csharp
// Daily backup during development
await Backup.Create();
// → Backs up all entities + vectors to .koan/backups/

// Before risky operation
await Backup<Media>.Create();
// → Quick entity-specific backup

// Restore if something breaks
await Backup<Media>.Restore(new RestoreOptions
{
    Timestamp = "latest"
});
```

### Provider Migration

```csharp
// Step 1: Export from Weaviate
await Backup.Create(new BackupOptions
{
    Include = new[] { "data", "vectors" }
});
// → Exports to .koan/backups/

// Step 2: Change .csproj
// <ProjectReference Include="Koan.Data.Vector.Connector.Weaviate.csproj" />
// becomes
// <ProjectReference Include="Koan.Data.Connector.ElasticSearch.csproj" />

// Step 3: Rebuild and restore
await Backup.Create(); // New container starts
await Backup.Restore(new RestoreOptions
{
    Timestamp = "2025-10-02T15-30-45Z"
});
// ✅ Entities → MongoDB (same)
// ✅ Vectors → ElasticSearch (different provider!)
// ✅ Zero AI regeneration (embeddings from cache)
```

### Selective Backup

```csharp
// Backup only high-value data
await Backup.CreateFor(
    new[] { typeof(Media) },
    new BackupOptions
    {
        Include = new[] { "data" }  // Skip vectors
    }
);

// Backup vectors only (unusual but possible)
await Backup<Media>.Create(new BackupOptions
{
    Include = new[] { "vectors" }
});
```

---

## Boot Report Integration

```
[INFO] Koan:backup system initialized
[INFO] Koan:backup   inventory validation
[INFO] Koan:backup     included: 12 entities
[INFO] Koan:backup       Media → encrypt=false, schema=true (via assembly scope)
[INFO] Koan:backup       User → encrypt=true, schema=true (via [EntityBackup])
[INFO] Koan:backup       LogEntry → encrypt=false, schema=false (via [EntityBackup])
[WARN] Koan:backup     uncovered: 2 entities (assembly scope: None)
[WARN] Koan:backup       UnmarkedEntity → no backup coverage
[INFO] Koan:backup     excluded: 1 entity (explicit opt-out)
[INFO] Koan:backup       SearchIndex → reason: "Derived view, rebuild from source"
[INFO] Koan:backup   sources: data, vectors
[INFO] Koan:backup   path: .koan/backups
```

---

## Project Structure

```
src/
├── Koan.Data.Backup/              # Core backup engine
│   ├── BackupService.cs
│   ├── Backup.cs                  # Static API
│   ├── BackupOptions.cs
│   ├── RestoreOptions.cs
│   ├── BackupResult.cs
│   ├── BackupCapabilities.cs
│   └── IConstraintManager.cs
│
└── Koan.Web.Backup/               # HTTP layer
    ├── Controllers/
    │   └── BackupController.cs
    └── Models/
        ├── BackupRequest.cs
        └── BackupResponse.cs
```

---

## Key Design Decisions

| Decision | Rationale |
|----------|-----------|
| **Explicit opt-in via attributes** | Prevents silent data loss from unbounded auto-discovery |
| **Assembly-level scope** | Balances convenience with safety (opt-in all vs explicit decoration) |
| **Startup inventory** | Early validation catches missing coverage before production |
| **Manifest integrity first** | Failed backups marked explicitly, restore refuses corrupted data |
| **Policy metadata in manifest** | Audit trail for encryption, schema, and other policies |
| **GET /backup/create** | Pragmatism over REST purity - administrative action, not resource |
| **Thin adapters** | Use existing `Data<T>` APIs, no backup-specific adapter code |
| **Optional constraints** | `IConstraintManager` interface for adapters that support it |
| **Cache format for vectors** | Enables zero-cost provider migration |
| **ISO timestamps** | Sortable, parseable, filesystem-safe |
| **`.koan/` namespace** | Framework-owned directory parallel to `.git/` |

---

## Benefits

1. **Explicit Intent**: Attributes make backup participation clear and auditable
2. **Early Validation**: Startup inventory catches missing coverage before data loss
3. **Flexible Policy**: Per-entity encryption, schema inclusion, and other policies
4. **Assembly Convenience**: Opt-in all entities with single assembly attribute
5. **Manifest Integrity**: Failed backups marked explicitly, no silent failures
6. **Provider Transparency**: Export from Weaviate, restore to ElasticSearch
7. **Thin Adapters**: No backup-specific adapter code needed
8. **Discoverable**: `/capabilities` shows exactly what's available
9. **Simple DX**: `GET /backup/create` for full backup
10. **Framework-Aligned**: Uses existing Koan patterns and APIs

---

## Future Extensions

### Potential Additions

- **Incremental backups**: Only changed entities
- **Compression formats**: `.zip`, `.tar.gz`
- **Remote storage**: S3, Azure Blob integration
- **Scheduled backups**: Via Koan.Scheduling
- **Roslyn analyzers**: Build-time warnings for entities missing `[EntityBackup]` in `BackupScope.None` assemblies
- **Retention policies**: Per-entity retention via `[EntityBackup(RetentionDays = 30)]`
- **Partition-aware backup**: Selective backup by partition key
- **Backup validation**: Verify integrity before restore with checksums

### Extension Points

```csharp
// Custom backup storage
public interface IBackupStorage
{
    Task WriteAsync(string path, Stream data);
    Task<Stream> ReadAsync(string path);
}

// Custom serialization format
public interface IBackupFormat
{
    Task SerializeAsync<T>(Stream target, IAsyncEnumerable<T> entities);
    IAsyncEnumerable<T> DeserializeAsync<T>(Stream source);
}
```

---

## Related Documents

- [Koan Framework Architecture](../architecture/OVERVIEW.md)
- [Data Layer Design](../architecture/DATA-LAYER.md)
- [Vector System Design](../architecture/VECTOR-SYSTEM.md)
- [Provider Transparency](../principles/PROVIDER-TRANSPARENCY.md)

---

**Next Steps**: Implementation in Koan.Data.Backup and Koan.Web.Backup






