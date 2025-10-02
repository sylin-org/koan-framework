# Koan Framework Backup System - Design Specification

**Version**: 1.0
**Status**: Design Specification
**Date**: 2025-10-02

## Overview

The Koan Backup system provides zero-configuration, discovery-based backup and restore capabilities for entities and vectors. It follows the framework's core principles: auto-discovery, provider transparency, and minimal scaffolding.

## Core Principles

### 1. Discovery Over Configuration
- Backup capabilities are **discovered** by introspecting registered services
- No manual registration required
- Adding a package reference automatically enables backup for that data source

### 2. Thin Adapters
- Adapters remain dumb storage implementations
- Backup orchestrates using existing `Data<T>` and `Vector<T>` APIs
- Optional constraint management via `IConstraintManager` interface

### 3. Provider Transparency
- Export from Weaviate, restore to ElasticSearch
- Vector embeddings cached in portable format
- Zero AI regeneration cost when switching providers

### 4. Pragmatic HTTP Semantics
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

## Discovery Mechanism

### Capability Detection

```csharp
public class BackupService
{
    private readonly IServiceProvider _services;
    private readonly IVectorService? _vectorService;

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

        // Discover all Entity<T> types
        var entityTypes = DiscoverEntityTypes();

        foreach (var entityType in entityTypes)
        {
            var sources = new List<string> { "data" };

            // Check if entity has vectors
            if (_vectorService?.TryGetRepository(entityType) != null)
            {
                sources.Add("vectors");
            }

            capabilities.Entities[entityType.Name] = new EntityBackupInfo
            {
                FullTypeName = entityType.FullName!,
                AvailableSources = sources
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
      "availableSources": ["data", "vectors"]
    },
    "User": {
      "fullTypeName": "S5.Recs.Models.User",
      "availableSources": ["data"]
    }
  }
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
[INFO] Koan:backup   sources: data, vectors
[INFO] Koan:backup   entities: 3 discovered
[INFO] Koan:backup     Media → data, vectors
[INFO] Koan:backup     User → data
[INFO] Koan:backup     Settings → data
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
| **GET /backup/create** | Pragmatism over REST purity - administrative action, not resource |
| **Discovery over configuration** | Auto-detect capabilities from registered services |
| **Thin adapters** | Use existing `Data<T>` APIs, no backup-specific adapter code |
| **Optional constraints** | `IConstraintManager` interface for adapters that support it |
| **Cache format for vectors** | Enables zero-cost provider migration |
| **ISO timestamps** | Sortable, parseable, filesystem-safe |
| **`.koan/` namespace** | Framework-owned directory parallel to `.git/` |

---

## Benefits

1. **Zero Configuration**: Add package → backup capability discovered
2. **Provider Transparency**: Export from Weaviate, restore to ElasticSearch
3. **Thin Adapters**: No backup-specific adapter code needed
4. **Discoverable**: `/capabilities` shows exactly what's available
5. **Simple DX**: `GET /backup/create` for full backup
6. **Framework-Aligned**: Uses existing Koan patterns and APIs

---

## Future Extensions

### Potential Additions

- **Incremental backups**: Only changed entities
- **Compression formats**: `.zip`, `.tar.gz`
- **Remote storage**: S3, Azure Blob integration
- **Encryption**: At-rest backup encryption
- **Scheduled backups**: Via Koan.Scheduling
- **Backup validation**: Verify integrity before restore

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
