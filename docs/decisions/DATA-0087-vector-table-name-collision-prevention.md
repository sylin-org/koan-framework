---
id: DATA-0087
slug: DATA-0087-vector-table-name-collision-prevention
domain: DATA
status: Proposed
date: 2025-11-13
title: Vector Table Name Collision Prevention via "_vector" Suffix
---

# ADR DATA-0087 - Vector Table Name Collision Prevention

## Summary

Add universal `"_vector"` suffix to all vector storage names BEFORE partition resolution to prevent table/collection name collisions when entities use both relational (`Entity<T>`) and vector (`Vector<T>`) storage in the same database system.

**Impact:** PostgreSQL-based vector providers (PGVector), SQLite-based vector extensions, and any provider storing vectors in the same physical database as entities.

## Problem Statement

### Collision Scenario

When an entity like `Media` uses both:
1. **Relational storage** via `Entity<Media>.Save()` → table `"media"` or `"media#partition1"`
2. **Vector storage** via `Vector<Media>.Save()` → collection/table determined by vector adapter

If the vector adapter stores in the **same database system** (e.g., PostgreSQL with pgvector extension), there's a table name collision:

```sql
-- Entity storage (PostgreSQL data adapter)
CREATE TABLE media (...);
CREATE TABLE media#partition1 (...);

-- Vector storage (PGVector adapter) - COLLISION!
CREATE TABLE media (...);         -- ❌ Already exists
CREATE TABLE media#partition1 (...); -- ❌ Already exists
```

### Current Architecture (DATA-0086)

```
VectorStorageNameRegistry.GetOrCompute<Media, string>()
  ↓
VectorConfigs.Get() → "pgvector"
  ↓
NamingComposer.Compose(PgVectorFactory, typeof(Media), partition, sp)
  ↓
PgVectorFactory.GetStorageName(typeof(Media)) → "media"
PgVectorFactory.GetConcretePartition("partition1") → "partition1"
  ↓
Result: "media#partition1" ← COLLISION with entity table!
```

### Why This Matters for PGVector

The proposed C02 PGVector adapter (from `COMPLETION-PLAN-B01-B03-C02.md`) would create tables like:

```sql
-- PGVector index creation (from completion plan)
CREATE TABLE media (
    id TEXT PRIMARY KEY,
    embedding vector(1536),
    metadata JSONB
);

CREATE INDEX ON media
USING ivfflat (embedding vector_cosine_ops)
WITH (lists = 100);
```

This collides with:
```sql
-- Entity storage
CREATE TABLE media (
    Id TEXT PRIMARY KEY,
    Title TEXT,
    FilePath TEXT,
    -- ... entity columns
);
```

**PostgreSQL error:** `ERROR: relation "media" already exists`

---

## Decision

### Universal "_vector" Suffix Strategy

Apply `"_vector"` suffix to **all vector storage names** at the `VectorStorageNameRegistry` layer, **before** partition resolution.

### Modified Architecture

```
VectorStorageNameRegistry.GetOrCompute<Media, string>()
  ↓
VectorConfigs.Get() → "pgvector"
  ↓
NamingComposer.ComposeVector(PgVectorFactory, typeof(Media), partition, sp)
  ↓
storageName = PgVectorFactory.GetStorageName(typeof(Media)) → "media"
vectorName = storageName + "_vector" → "media_vector"    // NEW STEP
  ↓
PgVectorFactory.GetConcretePartition("partition1") → "partition1"
  ↓
Result: "media_vector#partition1" ✅ No collision
```

### Naming Examples

| Entity | Partition | Data Table | Vector Table | Collision? |
|--------|-----------|------------|--------------|------------|
| `Media` | None | `media` | `media_vector` | ✅ No |
| `Media` | `proj-a` | `media#proj-a` | `media_vector#proj-a` | ✅ No |
| `Article` | `tenant-1` | `Article#tenant-1` | `Article_vector#tenant-1` | ✅ No |
| `Chunk` | `019abc...` | `Chunk#019abc...` | `Chunk_vector#019abc...` | ✅ No |

### Cross-Provider Impact

**PostgreSQL (PGVector):**
```sql
-- Entity
CREATE TABLE media (...);
-- Vector
CREATE TABLE media_vector (id TEXT, embedding vector(1536), ...);
```

**Weaviate (GraphQL collections):**
```
# Entity: N/A (Weaviate vector-only)
# Vector: "Media_vector" → sanitized → "Media_vector"
# No collision risk (different system), but consistent naming
```

**SQLite (hypothetical vector extension):**
```sql
-- Entity
CREATE TABLE Media (...);
-- Vector
CREATE TABLE Media_vector (id TEXT, embedding BLOB, ...);
```

**Milvus/Qdrant (dedicated vector DBs):**
```
# No collision risk (separate systems)
# But consistent naming: "media_vector" collection name
```

---

## Implementation Plan

### Phase 1: Update `NamingComposer`

**File:** `src/Koan.Data.Abstractions/Naming/NamingComposer.cs`

**Add new method:**
```csharp
/// <summary>
/// Compose vector storage name with universal "_vector" suffix.
/// Prevents table/collection name collisions when entities use both
/// relational (Entity&lt;T&gt;) and vector (Vector&lt;T&gt;) storage.
/// </summary>
/// <remarks>
/// Suffix is applied BEFORE partition resolution:
///   Entity:  "media#partition1"
///   Vector:  "media_vector#partition1"
/// </remarks>
public static string ComposeVector(
    INamingProvider provider,
    Type entityType,
    string? partition,
    IServiceProvider services)
{
    // Get base storage name and add "_vector" suffix
    var storageName = provider.GetStorageName(entityType, services).Trim();
    var vectorName = storageName + "_vector";

    // Trim and check partition
    var trimmedPartition = partition?.Trim();
    if (string.IsNullOrEmpty(trimmedPartition))
        return vectorName;

    // Compose with partition
    var concretePartition = provider.GetConcretePartition(trimmedPartition).Trim();
    return vectorName + provider.RepositorySeparator + concretePartition;
}
```

**Rationale:**
- **DRY:** Reuses partition composition logic from `Compose()`
- **Single Responsibility:** Suffix application is centralized
- **Testable:** Pure function with no side effects

---

### Phase 2: Update `VectorStorageNameRegistry`

**File:** `src/Koan.Data.Vector.Abstractions/Configuration/VectorStorageNameRegistry.cs`

**Change:**
```csharp
return Cache.GetOrAdd(key, _ =>
{
    var namingProvider = ResolveProvider(sp, provider);

    // OLD: Uses data-layer composition (collision risk)
    // return NamingComposer.Compose(namingProvider, typeof(TEntity), partition, sp);

    // NEW: Uses vector-layer composition (collision-safe)
    return NamingComposer.ComposeVector(namingProvider, typeof(TEntity), partition, sp);
});
```

**Impact:**
- **Breaking change:** Yes (vector table/collection names change)
- **Migration required:** Yes (see Phase 4)
- **Scope:** All vector adapters automatically inherit new naming

---

### Phase 3: Verify Adapter Compatibility

**No code changes required** - all adapters use `VectorStorageNameRegistry`.

**Verify these adapters:**
1. `WeaviateVectorRepository.cs` → `ClassName` property ✅
2. `MilvusVectorRepository.cs` → Collection name property ✅
3. Future `PostgresVectorRepository.cs` → Table name property ✅

**Example (Weaviate):**
```csharp
private string ClassName
{
    get
    {
        // DATA-0086: Use vector-specific naming registry
        // Automatically handles partitions via EntityContext
        return VectorStorageNameRegistry.GetOrCompute<TEntity, TKey>(_sp);
        // Before: "Media" or "Media_partition"
        // After:  "Media_vector" or "Media_vector_partition"
    }
}
```

---

### Phase 4: Migration Strategy

#### A. Development Databases (Zero Data)

**Action:** Drop and recreate
```bash
# Weaviate
curl -X DELETE http://localhost:8080/v1/schema/Media
# Auto-recreates as "Media_vector" on next operation

# PostgreSQL (future PGVector)
DROP TABLE IF EXISTS media;
# Auto-recreates as "media_vector" on next operation
```

#### B. Production Databases (With Data)

**Option 1: Rename Collections/Tables**

**Weaviate:**
```bash
# Export existing data
koan vector export Media --provider weaviate --output media_backup.json

# Delete old class
curl -X DELETE http://localhost:8080/v1/schema/Media

# Update framework code (deploys new naming)
# Auto-creates "Media_vector" class

# Re-import data
koan vector import media_backup.json --target Media
```

**PostgreSQL (future PGVector):**
```sql
-- Rename existing table
ALTER TABLE media RENAME TO media_vector;

-- Update framework code (deploys new naming)
-- Now uses "media_vector" table name automatically
```

**Option 2: Re-embed All Entities**

For systems with automatic embedding (AI-0020):
```csharp
// After deploying new naming
using (Vector<Media>.WithPartition("partition1"))
{
    await Vector<Media>.Flush(); // Clears old "Media" collection
    // Automatic re-embedding via EmbeddingWorker creates "Media_vector"
}
```

---

### Phase 5: Add Tests

**File:** `tests/Suites/Data/Core/Koan.Tests.Data.Core/Specs/Naming/VectorNamingCollision.Spec.cs`

```csharp
using Xunit;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Vector.Abstractions.Configuration;

namespace Koan.Tests.Data.Core.Specs.Naming;

public class VectorNamingCollisionSpec
{
    [Fact]
    public void VectorName_HasUniversalSuffix()
    {
        // Arrange
        var entityName = "Media";

        // Act
        var vectorName = VectorStorageNameRegistry.GetOrCompute<Media, string>(sp);

        // Assert
        Assert.EndsWith("_vector", vectorName.Split('#')[0]);
    }

    [Fact]
    public void VectorName_WithPartition_SuffixBeforeSeparator()
    {
        // Arrange
        using (EntityContext.Partition("proj-123"))
        {
            // Act
            var dataName = StorageNameRegistry.GetOrCompute<Media, string>(sp);
            var vectorName = VectorStorageNameRegistry.GetOrCompute<Media, string>(sp);

            // Assert
            Assert.Equal("media#proj-123", dataName);
            Assert.Equal("media_vector#proj-123", vectorName);
        }
    }

    [Fact]
    public void NamingComposer_ComposeVector_AddsSuffix()
    {
        // Arrange
        var provider = new FakeNamingProvider
        {
            StorageName = "articles",
            Separator = "#",
            ConcretePartition = "tenant-456"
        };

        // Act
        var result = NamingComposer.ComposeVector(
            provider,
            typeof(Article),
            partition: "tenant-456",
            sp);

        // Assert
        Assert.Equal("articles_vector#tenant-456", result);
    }

    [Theory]
    [InlineData(null, "media_vector")]
    [InlineData("", "media_vector")]
    [InlineData("  ", "media_vector")]
    [InlineData("p1", "media_vector#p1")]
    public void NamingComposer_ComposeVector_HandlesPartitions(
        string? partition,
        string expected)
    {
        // Arrange
        var provider = new FakeNamingProvider
        {
            StorageName = "media",
            Separator = "#"
        };

        // Act
        var result = NamingComposer.ComposeVector(
            provider,
            typeof(Media),
            partition,
            sp);

        // Assert
        Assert.Equal(expected, result);
    }
}
```

---

### Phase 6: Update Documentation

**Files to update:**
1. `docs/decisions/DATA-0086-unified-naming-provider-interface.md`
   - Add note about vector suffix in "Post-Implementation Correction" section
   - Update examples to show `_vector` suffix

2. `docs/guides/embedding-best-practices.md`
   - Add note about automatic `_vector` suffix in naming
   - Update troubleshooting section for migration

3. `docs/proposals/koan-dotnet10-opportunity-map/COMPLETION-PLAN-B01-B03-C02.md`
   - Update PGVector implementation section with correct table names
   - Fix code example from completion plan (line 224-234)

**Before (incorrect):**
```csharp
public async Task CreateIvfflatIndexAsync(
    string tableName,  // ❌ Would collide with entity table
    int lists = 100)
{
    await conn.ExecuteAsync($@"
        CREATE INDEX ON {tableName}
        USING ivfflat (embedding vector_cosine_ops)
        WITH (lists = {lists})
    ");
}
```

**After (correct):**
```csharp
public async Task CreateIvfflatIndexAsync(
    string tableName,  // ✅ Automatically includes "_vector" suffix
    int lists = 100)
{
    // tableName = "media_vector" or "media_vector#partition1"
    await conn.ExecuteAsync($@"
        CREATE INDEX ON {tableName}
        USING ivfflat (embedding vector_cosine_ops)
        WITH (lists = {lists})
    ");
}
```

---

## Risk Assessment

### 🔴 High Risk

**Breaking Change - Vector Table/Collection Names Change**
- **Mitigation:** Provide migration tooling via `koan vector export/import`
- **Timeline:** Announce 60 days before release, include in major version bump
- **Escape Hatch:** Allow opt-out via `VectorOptions.DisableVectorSuffix = true` (temporary)

### 🟡 Medium Risk

**Migration Complexity for Production Systems**
- **Mitigation:** Comprehensive migration guide with per-provider instructions
- **Timeline:** Document all migration paths before release
- **Escape Hatch:** Support parallel operation (old + new names) for 1 release cycle

### 🟢 Low Risk

**Development/Testing Environments**
- **Mitigation:** Drop and recreate (zero data loss risk)
- **Timeline:** Immediate adoption

---

## Alternatives Considered

### Alternative 1: Provider-Specific Collision Detection

Each adapter implements collision detection:
```csharp
public class PostgresVectorRepository
{
    private async Task EnsureNoCollision(string tableName, CancellationToken ct)
    {
        // Check if entity table exists
        var exists = await conn.ExecuteScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM pg_tables WHERE tablename = @name)",
            new { name = tableName });

        if (exists)
            throw new InvalidOperationException($"Table {tableName} already exists");
    }
}
```

**Rejected because:**
- ❌ Runtime errors instead of prevention
- ❌ Requires per-adapter implementation
- ❌ No solution, just detection
- ❌ Breaks provider transparency (adapter-specific behavior)

### Alternative 2: Separate Schema/Namespace

Use PostgreSQL schemas:
```sql
CREATE SCHEMA vectors;
CREATE TABLE vectors.media (...);  -- Vector storage
CREATE TABLE public.media (...);   -- Entity storage
```

**Rejected because:**
- ❌ PostgreSQL-specific (doesn't help SQLite, etc.)
- ❌ Increases configuration complexity
- ❌ Breaks Koan's "zero-config" principle
- ❌ Requires user to manage schema permissions

### Alternative 3: Prefix Instead of Suffix

Use `"vector_"` prefix:
```
Entity:  "media#partition1"
Vector:  "vector_media#partition1"
```

**Rejected because:**
- ❌ Less intuitive (suffix reads better: "this is media data, for vectors")
- ❌ Breaks alphabetical sorting (vectors scattered among entities)
- ✅ Would work technically, but suffix preferred for UX

### Alternative 4: Hash-Based Unique Names

Generate unique names via hashing:
```csharp
var vectorName = $"{storageName}_{Hash(entityType.FullName).Substring(0, 8)}";
// Result: "media_a3f4d9e2"
```

**Rejected because:**
- ❌ Non-deterministic appearance (hard to debug)
- ❌ Obscures intent (what is "a3f4d9e2"?)
- ❌ Complicates manual database inspection
- ❌ No semantic value

---

## Success Criteria

✅ **Zero Collisions:** PGVector adapter creates tables without conflicts
✅ **Universal Application:** All vector adapters use `_vector` suffix
✅ **Partition Compatibility:** Suffix applied before partition resolution
✅ **Migration Path:** Production systems can upgrade without data loss
✅ **Tests Pass:** All existing + new collision prevention tests pass
✅ **Documentation Complete:** Migration guide and naming conventions documented

---

## Implementation Checklist

### Core Infrastructure
- [ ] Add `NamingComposer.ComposeVector()` method
- [ ] Update `VectorStorageNameRegistry` to use `ComposeVector()`
- [ ] Add unit tests for `ComposeVector()` logic
- [ ] Add integration tests for collision prevention

### Migration Tooling
- [ ] Add `koan vector export` command (if not exists)
- [ ] Add `koan vector import` command (if not exists)
- [ ] Document per-provider migration steps
- [ ] Create migration validation script

### Documentation
- [ ] Update DATA-0086 ADR with vector suffix note
- [ ] Update embedding best practices guide
- [ ] Update COMPLETION-PLAN-B01-B03-C02.md with correct table names
- [ ] Create migration guide: `docs/guides/vector-naming-migration.md`

### Validation
- [ ] Build entire solution (zero errors)
- [ ] Run all tests (zero failures)
- [ ] Test Weaviate adapter with new naming
- [ ] Test future PGVector adapter (create mock)
- [ ] Verify partition composition works correctly

### Communication
- [ ] Announce breaking change in release notes
- [ ] Add migration checklist to upgrade guide
- [ ] Create GitHub discussion for community feedback

---

## References

**Related ADRs:**
- DATA-0086: Unified INamingProvider for Storage and Partition Naming
- DATA-0077: EntityContext Partition Routing
- AI-0020: Entity-First AI and Transaction Coordination

**Proposal Context:**
- COMPLETION-PLAN-B01-B03-C02.md (C02 PGVector adapter implementation)
- STRATEGIC-DASHBOARD.md (90-day execution plan)

**Files to Create:**
- `src/Koan.Data.Abstractions/Naming/NamingComposer.cs` (add `ComposeVector()`)
- `tests/Suites/Data/Core/Koan.Tests.Data.Core/Specs/Naming/VectorNamingCollision.Spec.cs`
- `docs/guides/vector-naming-migration.md`

**Files to Modify:**
- `src/Koan.Data.Vector.Abstractions/Configuration/VectorStorageNameRegistry.cs`
- `docs/decisions/DATA-0086-unified-naming-provider-interface.md`
- `docs/guides/embedding-best-practices.md`
- `docs/proposals/koan-dotnet10-opportunity-map/COMPLETION-PLAN-B01-B03-C02.md`

---

## Appendix: Impact on C02 PGVector Implementation

### Updated Table Schema (from COMPLETION-PLAN)

**Before (collision risk):**
```sql
CREATE TABLE media (  -- ❌ Collides with entity table
    id TEXT PRIMARY KEY,
    embedding vector(1536),
    metadata JSONB
);
```

**After (collision-safe):**
```sql
CREATE TABLE media_vector (  -- ✅ No collision
    id TEXT PRIMARY KEY,
    embedding vector(1536),
    metadata JSONB
);

CREATE INDEX ON media_vector
USING ivfflat (embedding vector_cosine_ops)
WITH (lists = 100);
```

### PostgresVectorRepository Implementation

```csharp
internal sealed class PostgresVectorRepository<TEntity, TKey>
    : IVectorSearchRepository<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly IServiceProvider _sp;

    private string TableName
    {
        get
        {
            // Uses VectorStorageNameRegistry → automatic "_vector" suffix
            return VectorStorageNameRegistry.GetOrCompute<TEntity, TKey>(_sp);
            // Result: "media_vector" or "media_vector#partition1"
        }
    }

    public async Task VectorEnsureCreatedAsync(CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        // Enable pgvector extension
        await conn.ExecuteAsync("CREATE EXTENSION IF NOT EXISTS vector");

        // Create vector table (no collision with entity table!)
        await conn.ExecuteAsync($@"
            CREATE TABLE IF NOT EXISTS {TableName} (
                id TEXT PRIMARY KEY,
                embedding vector(1536),
                metadata JSONB
            )
        ");
    }
}
```

### Sample Usage (S5.Recs)

```csharp
// Entity storage
var media = new Media
{
    Title = "Inception",
    FilePath = "/movies/inception.mp4"
};
await media.Save();  // → Table: "media"

// Vector storage
var embedding = await Ai.FromText(media.Title).ToEmbedding();
await Vector<Media>.Save(media.Id, embedding);  // → Table: "media_vector"

// Both tables coexist in PostgreSQL:
// SELECT * FROM media;         -- Entity data
// SELECT * FROM media_vector;  -- Vector embeddings
```

---

**Decision Status:** Proposed
**Next Steps:** Review by platform architecture team → Approval → Implementation (Phase 1-6)
**Target Release:** v0.7.0 (Major version due to breaking change)
**Migration Window:** 60 days advance notice, tooling support, parallel operation for 1 release
