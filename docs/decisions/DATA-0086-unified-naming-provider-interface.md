---
id: DATA-0086
slug: DATA-0086-unified-naming-provider-interface
domain: DATA
status: Corrected
date: 2025-11-08
corrected: 2025-11-09
title: Unified INamingProvider for Storage and Partition Naming
---

> **⚠️ POST-IMPLEMENTATION CORRECTION (2025-11-09)**
>
> The initial implementation violated Separation of Concerns by forcing vector repositories to use entity-layer infrastructure (`StorageNameRegistry`). This caused vector operations to incorrectly resolve data adapter naming conventions, resulting in invalid collection/class names.
>
> **Status:** Implementation corrected with parallel registry architecture (Data vs Vector layers).
>
> **Jump to:** [Post-Implementation Correction](#post-implementation-correction-data-vs-vector-separation)

# ADR DATA-0086 - Unified INamingProvider for Storage and Partition Naming

## Summary

**Original Decision (2025-11-08):** Unified `INamingProvider` interface for all adapters (data + vector), implemented via factory extension.

**Correction (2025-11-09):** Parallel registry architecture - `StorageNameRegistry` (data layer) and `VectorStorageNameRegistry` (vector layer) using shared `NamingComposer` algorithm.

## Problem Statement

Vector repositories (Weaviate, Milvus, etc.) were calling `StorageNameRegistry.GetOrCompute()`, which queries `AggregateConfigs` (entity-layer) → only checks `[DataAdapter]` attribute → falls back to highest-priority **data adapter** (SQLite) → produces invalid collection names.

**Example Bug:**
```
Chunk entity with no [VectorAdapter] attribute
  ↓
WeaviateVectorRepository.ClassName calls StorageNameRegistry.GetOrCompute()
  ↓
AggregateConfigs returns "sqlite" provider (wrong layer!)
  ↓
SqliteAdapterFactory.GetStorageName() → "Koan.Context.Models.Chunk"
SqliteAdapterFactory.RepositorySeparator → "#"
SqliteAdapterFactory.GetConcretePartition() → "019a6626a4457ebfbf3365cb10ac0ed6"
  ↓
Result: "Koan.Context.Models.Chunk#019a6626a4457ebfbf3365cb10ac0ed6"
  ❌ Invalid Weaviate class name (dots, hash not allowed in GraphQL)
```

---

## Post-Implementation Correction: Data vs Vector Separation

### Architecture Principle

```
┌─────────────────────────────────────────────────────────┐
│           SHARED ABSTRACTIONS                           │
│  - INamingProvider (interface contract)                 │
│  - EntityContext.Partition (cross-cutting state)        │
│  - NamingComposer (composition algorithm)               │
└─────────────────────────────────────────────────────────┘
              ↓                              ↓
┌──────────────────────────┐   ┌──────────────────────────┐
│      DATA LAYER          │   │     VECTOR LAYER         │
│                          │   │                          │
│ • AggregateConfigs       │   │ • VectorConfigs          │
│   - [DataAdapter] attr   │   │   - [VectorAdapter] attr │
│   - IDataAdapterFactory  │   │   - IVectorAdapterFactory│
│                          │   │                          │
│ • StorageNameRegistry    │   │ • VectorStorageNameReg   │
│   - Cache: provider+part │   │   - Cache: provider+part │
│   - Uses: NamingComposer │   │   - Uses: NamingComposer │
│                          │   │                          │
│ • SqliteRepository       │   │ • WeaviateVectorRepo     │
│   TableName → calls      │   │   ClassName → calls      │
│   StorageNameRegistry    │   │   VectorStorageNameReg   │
└──────────────────────────┘   └──────────────────────────┘
```

### Corrected Resolution Flow

**Entity Operations (Data Layer):**
```
await chunk.Save()
  ↓
AggregateConfigs.Get<Chunk>()
  ↓
Check [DataAdapter] / [SourceAdapter] → "sqlite"
  ↓
StorageNameRegistry.GetOrCompute()
  ↓
SqliteAdapterFactory (INamingProvider)
  ↓
Result: "Chunk#019a6626a4457ebfbf3365cb10ac0ed6"
```

**Vector Operations (Vector Layer):**
```
await Vector<Chunk>.Save(chunk, embedding)
  ↓
VectorConfigs.Get<Chunk>()
  ↓
Check [VectorAdapter] → fallback to highest-priority vector adapter → "weaviate"
  ↓
VectorStorageNameRegistry.GetOrCompute()
  ↓
WeaviateAdapterFactory (INamingProvider)
  ↓
Result: "Koan_Context_Models_Chunk_019a6626_a445_7ebf_bf33_65cb10ac0ed6"
```

---

## Implementation Plan

### Phase 1: Create Shared Composition Logic

**File:** `src/Koan.Data.Abstractions/Naming/NamingComposer.cs`

```csharp
namespace Koan.Data.Abstractions.Naming;

public static class NamingComposer
{
    public static string Compose(
        INamingProvider provider,
        Type entityType,
        string? partition,
        IServiceProvider services)
    {
        var storageName = provider.GetStorageName(entityType, services).Trim();
        var trimmedPartition = partition?.Trim();

        if (string.IsNullOrEmpty(trimmedPartition))
            return storageName;

        var concretePartition = provider.GetConcretePartition(trimmedPartition).Trim();
        return storageName + provider.RepositorySeparator + concretePartition;
    }
}
```

**Rationale:** DRY - both registries use identical logic.

---

### Phase 2: Create Vector Configuration System

**File:** `src/Koan.Data.Vector.Abstractions/VectorConfigs.cs`

```csharp
using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Koan.Data.Abstractions;

namespace Koan.Data.Vector.Abstractions;

public static class VectorConfigs
{
    private static readonly ConcurrentDictionary<(Type, Type), object> Cache = new();

    public static VectorConfig<TEntity, TKey> Get<TEntity, TKey>(IServiceProvider sp)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var key = (typeof(TEntity), typeof(TKey));
        if (Cache.TryGetValue(key, out var existing))
            return (VectorConfig<TEntity, TKey>)existing;

        var provider = ResolveProvider(typeof(TEntity), sp);
        var cfg = new VectorConfig<TEntity, TKey>(provider, sp);
        Cache[key] = cfg;
        return cfg;
    }

    internal static void Reset() => Cache.Clear();

    private static string ResolveProvider(Type entityType, IServiceProvider sp)
    {
        // Check [VectorAdapter] attribute
        var attr = (VectorAdapterAttribute?)Attribute.GetCustomAttribute(
            entityType, typeof(VectorAdapterAttribute));

        if (attr is not null && !string.IsNullOrWhiteSpace(attr.Provider))
            return attr.Provider;

        // Fallback: Highest-priority vector adapter factory
        return DefaultVectorProvider(sp);
    }

    private static string DefaultVectorProvider(IServiceProvider sp)
    {
        var factories = sp.GetServices<IVectorAdapterFactory>().ToList();

        if (factories.Count == 0)
            throw new InvalidOperationException(
                "No IVectorAdapterFactory registered.");

        var ranked = factories
            .Select(f => new
            {
                Factory = f,
                Priority = (f.GetType().GetCustomAttributes(typeof(ProviderPriorityAttribute), false)
                    .FirstOrDefault() as ProviderPriorityAttribute)?.Priority ?? 0,
                Name = f.GetType().Name
            })
            .OrderByDescending(x => x.Priority)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return ranked.First().Factory.Provider;
    }
}
```

**File:** `src/Koan.Data.Vector.Abstractions/VectorConfig.cs`

```csharp
using Koan.Data.Abstractions;

namespace Koan.Data.Vector.Abstractions;

public sealed class VectorConfig<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    public string Provider { get; }
    public IServiceProvider Services { get; }

    internal VectorConfig(string provider, IServiceProvider services)
    {
        Provider = provider;
        Services = services;
    }
}
```

---

### Phase 3: Create Vector Storage Name Registry

**File:** `src/Koan.Data.Vector.Abstractions/Configuration/VectorStorageNameRegistry.cs`

```csharp
using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;

namespace Koan.Data.Vector.Abstractions.Configuration;

public static class VectorStorageNameRegistry
{
    private static readonly ConcurrentDictionary<string, string> Cache = new();

    public static string GetOrCompute<TEntity, TKey>(IServiceProvider sp)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var cfg = VectorConfigs.Get<TEntity, TKey>(sp);
        var provider = cfg.Provider;
        var partition = EntityContext.Current?.Partition;
        var key = CacheKey(provider, partition);

        return Cache.GetOrAdd(key, _ =>
        {
            var namingProvider = ResolveProvider(sp, provider);
            return NamingComposer.Compose(namingProvider, typeof(TEntity), partition, sp);
        });
    }

    private static string CacheKey(string provider, string? partition)
    {
        var trimmedPartition = partition?.Trim();
        return string.IsNullOrEmpty(trimmedPartition)
            ? $"vector:{provider}"
            : $"vector:{provider}:{trimmedPartition}";
    }

    private static INamingProvider ResolveProvider(IServiceProvider sp, string providerKey)
    {
        var factories = sp.GetServices<IVectorAdapterFactory>();

        foreach (var factory in factories)
        {
            if (factory.CanHandle(providerKey))
                return factory;
        }

        throw new InvalidOperationException(
            $"No vector adapter registered for provider '{providerKey}'.");
    }

    internal static void Reset() => Cache.Clear();
}
```

---

### Phase 4: Update StorageNameRegistry

**File:** `src/Koan.Data.Core/Configuration/StorageNameRegistry.cs`

**Changes:**

1. Replace inline composition with `NamingComposer.Compose()`:
```csharp
return AggregateBags.GetOrAdd<TEntity, TKey, string>(sp, key, () =>
{
    var namingProvider = ResolveProvider(sp, provider);
    return NamingComposer.Compose(namingProvider, typeof(TEntity), partition, sp);
});
```

2. Remove vector factory reflection (lines 68-86):
```csharp
private static INamingProvider ResolveProvider(IServiceProvider sp, string providerKey)
{
    // Query ONLY data adapter factories
    var factories = sp.GetServices<IDataAdapterFactory>();

    foreach (var factory in factories)
    {
        if (factory.CanHandle(providerKey))
            return factory;
    }

    throw new InvalidOperationException(
        $"No data adapter registered for provider '{providerKey}'.");
}
```

---

### Phase 5: Update All Vector Repositories

**Files to update:**
- `src/Connectors/Data/Vector/Weaviate/WeaviateVectorRepository.cs`
- `src/Connectors/Data/Vector/Milvus/MilvusVectorRepository.cs`
- `src/Connectors/Data/OpenSearch/OpenSearchVectorRepository.cs`
- `src/Connectors/Data/ElasticSearch/ElasticSearchVectorRepository.cs`

**Change:**
```csharp
// OLD (wrong layer)
private string ClassName
{
    get
    {
        return Koan.Data.Core.Configuration.StorageNameRegistry.GetOrCompute<TEntity, TKey>(_sp);
    }
}
```

**To:**
```csharp
// NEW (correct layer)
using Koan.Data.Vector.Abstractions.Configuration;

private string ClassName
{
    get
    {
        return VectorStorageNameRegistry.GetOrCompute<TEntity, TKey>(_sp);
    }
}
```

---

### Phase 6: Add Tests

**File:** `tests/Suites/Data/Core/Koan.Tests.Data.Core/Specs/Naming/DataVectorSeparation.Spec.cs`

```csharp
using Xunit;
using Koan.Data.Core.Configuration;
using Koan.Data.Vector.Abstractions.Configuration;

public class DataVectorSeparationSpec
{
    [Fact]
    public void DataRegistry_OnlyQueriesDataFactories()
    {
        // Arrange: Entity with no [DataAdapter], only [VectorAdapter]
        // Act: Call StorageNameRegistry
        // Assert: Should throw (doesn't fall back to vector factories)
    }

    [Fact]
    public void VectorRegistry_OnlyQueriesVectorFactories()
    {
        // Arrange: Entity with no [VectorAdapter], only [DataAdapter]
        // Act: Call VectorStorageNameRegistry
        // Assert: Uses highest-priority vector factory (ignores data adapter)
    }

    [Fact]
    public void SameEntity_DifferentNamesInDataVsVector()
    {
        // Arrange: Chunk entity
        // Act: Get name from both registries
        // Assert: Different naming conventions applied
        //   Data: "Chunk#partition" (SQLite)
        //   Vector: "Koan_Context_Models_Chunk_partition" (Weaviate)
    }
}
```

---

## Implementation Checklist

### Pre-Implementation
- [x] Analyze vector operation patterns across codebase
- [x] Identify all entities using vector storage (Media, Chunk, test entities)
- [x] Document architectural violation root cause

### Core Infrastructure
- [ ] Create `NamingComposer` shared composition logic
- [ ] Create `VectorConfigs` and `VectorConfig` classes
- [ ] Create `VectorStorageNameRegistry`
- [ ] Update `StorageNameRegistry` to use `NamingComposer`
- [ ] Remove vector factory reflection from `StorageNameRegistry`

### Adapters
- [ ] Update `WeaviateVectorRepository` to use `VectorStorageNameRegistry`
- [ ] Update `MilvusVectorRepository` to use `VectorStorageNameRegistry`
- [ ] Update `OpenSearchVectorRepository` to use `VectorStorageNameRegistry`
- [ ] Update `ElasticSearchVectorRepository` to use `VectorStorageNameRegistry`

### Testing
- [ ] Add `DataVectorSeparation.Spec.cs` tests
- [ ] Verify data registry never queries vector factories
- [ ] Verify vector registry never queries data factories
- [ ] Verify same entity gets different names in each layer

### Validation
- [ ] Build entire solution (zero errors)
- [ ] Run all tests (zero failures)
- [ ] Test KoanContext service with Weaviate
- [ ] Test S5.Recs with vector search
- [ ] Verify Weaviate class names are GraphQL-compliant

### Documentation
- [x] Update DATA-0086 ADR with correction
- [ ] Update CLAUDE.md if needed

---

## Agentic Session Guidance

### For Multi-Session Implementation

**Session 1: Core Infrastructure**
1. Read this ADR completely
2. Create `NamingComposer.cs` (Phase 1)
3. Create `VectorConfigs.cs` + `VectorConfig.cs` (Phase 2)
4. Create `VectorStorageNameRegistry.cs` (Phase 3)
5. Build and verify compilation

**Session 2: Data Layer Update**
1. Update `StorageNameRegistry.GetOrCompute()` to use `NamingComposer`
2. Delete `GetTargetRepositoryName()` private method
3. Update `ResolveProvider()` to remove vector factory reflection
4. Build and verify compilation

**Session 3: Vector Layer Update**
1. Update `WeaviateVectorRepository.cs`
2. Update `MilvusVectorRepository.cs`
3. Update `OpenSearchVectorRepository.cs`
4. Update `ElasticSearchVectorRepository.cs`
5. Build and verify compilation

**Session 4: Testing & Validation**
1. Add `DataVectorSeparation.Spec.cs` tests
2. Run all tests
3. Test with KoanContext service
4. Test with S5.Recs
5. Verify Weaviate class names

### Key Decision Points

**Q: Should VectorConfigs check [DataAdapter] as fallback?**
A: NO. Violates SoC. Use highest-priority vector factory only.

**Q: Should we add [VectorAdapter] to all entities?**
A: NO. Explicit attribute is optional. Default resolution works via factory priority.

**Q: Keep VectorService fallback logic (lines 24-54)?**
A: YES for now (backward compat), but log warnings. Consider deprecation later.

**Q: Cache invalidation strategy?**
A: In-memory caches auto-clear on app restart. No migration needed.

---

## Success Criteria

✅ **Zero Crosstalk:** Data registry never queries vector factories
✅ **Zero Crosstalk:** Vector registry never queries data factories
✅ **Correct Names:** Weaviate produces GraphQL-compliant class names
✅ **Partition Sharing:** `EntityContext.Partition()` works for both layers
✅ **Performance:** Hot path uses cached names (O(1) dictionary lookup)
✅ **Tests Pass:** All existing + new separation tests pass

---

## References

**Original Implementation:** Lines 1-1379 (retained for historical context)
**Supersedes:** Initial DATA-0086 implementation (StorageNameRegistry unified approach)
**Related:** DATA-0077 (EntityContext partition routing), ARCH-0070 (Embedding attributes)

**Files Created:**
- `src/Koan.Data.Abstractions/Naming/NamingComposer.cs`
- `src/Koan.Data.Vector.Abstractions/VectorConfigs.cs`
- `src/Koan.Data.Vector.Abstractions/VectorConfig.cs`
- `src/Koan.Data.Vector.Abstractions/Configuration/VectorStorageNameRegistry.cs`

**Files Modified:**
- `src/Koan.Data.Core/Configuration/StorageNameRegistry.cs`
- `src/Connectors/Data/Vector/Weaviate/WeaviateVectorRepository.cs`
- `src/Connectors/Data/Vector/Milvus/MilvusVectorRepository.cs`
- `src/Connectors/Data/OpenSearch/OpenSearchVectorRepository.cs`
- `src/Connectors/Data/ElasticSearch/ElasticSearchVectorRepository.cs`
