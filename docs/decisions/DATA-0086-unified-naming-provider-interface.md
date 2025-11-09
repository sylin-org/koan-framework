---
id: DATA-0086
slug: DATA-0086-unified-naming-provider-interface
domain: DATA
status: Accepted
date: 2025-11-08
title: Unified INamingProvider for Storage and Partition Naming
---

# ADR DATA-0086 - Unified INamingProvider for Storage and Partition Naming

## Context

### Current Architecture Fragmentation

The framework currently splits storage naming and partition formatting across multiple interfaces and mechanisms, creating architectural fragmentation:

1. **INamingDefaultsProvider** (Koan.Data.Abstractions/Naming/INamingDefaultsProvider.cs)
   - Provides storage naming conventions (Style, Separator, Casing)
   - Provides partition formatting via `FormatPartitionId(string)`
   - Mixed responsibilities: base naming + partition formatting in one interface

2. **IVectorPartitionMapper** (Weaviate-specific)
   - Separate partition handling for Weaviate vector provider
   - Duplicates partition formatting logic
   - Inconsistent with other adapters

3. **Hardcoded Separator** (StorageNameRegistry.AppendSet:59)
   ```csharp
   return baseName + "#" + formattedSet;  // Hardcoded "#"
   ```
   - No provider flexibility for separator choice
   - Weaviate requires `_` for GraphQL compatibility

4. **Disabled Partition Validation** (EntityContext.cs:125)
   ```csharp
   // newContext.ValidatePartitionName();  // Commented out
   ```
   - No validation framework for partition names
   - Validation logic exists but unused

5. **"root" Special Case** (StorageNameRegistry.BagKey:14)
   ```csharp
   string.Equals(set, "root", StringComparison.OrdinalIgnoreCase)
   ```
   - Prevents users from creating legitimate "root" partition
   - Artificial restriction without clear rationale

### Problems with Current Design

**Mixed Responsibilities:**
- `INamingDefaultsProvider` handles both storage conventions AND partition formatting
- Unclear which interface to implement for partition-only customization
- Adapter override mechanism (`GetAdapterOverride`) only for storage names, not partitions

**Inconsistent Patterns:**
- Weaviate has TWO partition mechanisms (INamingDefaultsProvider + IVectorPartitionMapper)
- Other adapters only use INamingDefaultsProvider
- No clear guidance on which to use

**Inflexibility:**
- Hardcoded `#` separator prevents provider-specific requirements
- Weaviate needs `_` for GraphQL class names
- MongoDB/Postgres could use different separators for namespacing

**Incomplete Partition Support:**
- Partition formatting exists but no validation
- No trimming of partition names (whitespace handling)
- Special "root" case prevents legitimate use

**Example Resolution Flow Issues:**
```
Entity<Todo> + partition "019a6626-a445-7ebf-bf33-65cb10ac0ed6"

Current (SQLite):
1. GetConvention() → (FullNamespace, ".", AsIs)
2. Resolve() → "MyApp.Todo"
3. FormatPartitionId() → "proj-019a6626a4457ebfbf3365cb10ac0ed6"
4. Hardcoded AppendSet() → "MyApp.Todo#proj-019a6626a4457ebfbf3365cb10ac0ed6"
   ❌ Adapter can't control separator
   ❌ "proj-" prefix is provider-specific but mixed with framework composition

Desired (SQLite):
1. GetStorageName() → "MyApp.Todo"
2. GetConcretePartition() → "6caab928395248a1ac60b1d2a1245c9e"
3. RepositorySeparator → "#"
4. Framework composes → "MyApp.Todo#6caab928395248a1ac60b1d2a1245c9e"
   ✅ Adapter controls all components
   ✅ Clear separation: storage name vs partition identifier
```

## Decision

### 1. Merge INamingProvider into IDataAdapterFactory

**Rationale:** Naming is adapter-specific and inseparable from the adapter contract. Making `IDataAdapterFactory` extend `INamingProvider` ensures:
- ✅ Compile-time enforcement (cannot create factory without implementing naming)
- ✅ Single object, single responsibility (complete adapter contract)
- ✅ Impossible to forget (no separate registration needed)
- ✅ Simpler testing (one object to mock)

```csharp
namespace Koan.Data.Abstractions.Naming;

/// <summary>
/// Provider-specific naming strategy for storage and partition identifiers.
/// Implemented by IDataAdapterFactory - all adapters MUST provide naming logic.
/// </summary>
public interface INamingProvider
{
    /// <summary>Provider key (e.g., "sqlite", "mongo", "weaviate").</summary>
    string Provider { get; }

    /// <summary>
    /// Get base storage name for entity type.
    /// Framework caches result per (entity, provider).
    ///
    /// Adapter can implement any logic:
    /// - Respect [Storage(Name)] / [StorageName] attributes
    /// - Apply adapter-specific conventions (e.g., MongoOptions.CollectionName)
    /// - Use StorageNameResolver.Resolve() for convention-based naming
    /// - Custom logic (legacy prefixes, multi-table mapping, etc.)
    ///
    /// Framework trims output before final composition.
    /// </summary>
    string GetStorageName(Type entityType, IServiceProvider services);

    /// <summary>
    /// Format abstract partition name into concrete provider-specific identifier.
    /// Pure function - no caching, no side effects.
    ///
    /// Framework trims output before final composition.
    ///
    /// Examples for partition "6caab928-3952-48a1-ac60-b1d2a1245c9e":
    /// - SQLite:   "6caab928395248a1ac60b1d2a1245c9e" (remove hyphens)
    /// - Weaviate: "6caab928_3952_48a1_ac60_b1d2a1245c9e" (hyphens to underscores)
    /// - MongoDB:  "6caab928-3952-48a1-ac60-b1d2a1245c9e" (pass-through)
    /// </summary>
    string GetConcretePartition(string partition);

    /// <summary>
    /// Separator between storage name and partition identifier.
    ///
    /// Examples:
    /// - SQLite:   "#" → "MyApp.Todo#6caab928395248a1ac60b1d2a1245c9e"
    /// - Weaviate: "_" → "MyApp_Todo_6caab928_3952_48a1_ac60_b1d2a1245c9e"
    /// - MongoDB:  "#" → "MyApp.Todo#6caab928-3952-48a1-ac60-b1d2a1245c9e"
    /// </summary>
    string RepositorySeparator { get; }
}
```

```csharp
namespace Koan.Data.Abstractions;

/// <summary>
/// Complete data adapter contract: repository creation and storage naming.
/// Each adapter must implement both concerns.
/// </summary>
public interface IDataAdapterFactory : INamingProvider
{
    // Provider property inherited from INamingProvider

    bool CanHandle(string provider);

    IDataRepository<TEntity, TKey> Create<TEntity, TKey>(
        IServiceProvider sp,
        string source = "Default")
        where TEntity : class, IEntity<TKey>
        where TKey : notnull;
}
```

### 2. Single Orchestration Method in StorageNameRegistry

```csharp
/// <summary>
/// Orchestrates full repository name resolution.
/// Composes: [StorageName] or [StorageName][Separator][ConcretePartition]
///
/// Trimming rules:
/// - Storage name trimmed before composition
/// - Partition trimmed before evaluation
/// - Concrete partition trimmed before composition
/// - Separator used as-is (not trimmed)
/// </summary>
private static string GetTargetRepositoryName<TEntity>(
    INamingProvider np,
    string? partition,
    IServiceProvider services)
{
    // Get and trim storage name
    var storageName = np.GetStorageName(typeof(TEntity), services).Trim();

    // Trim and check partition
    var trimmedPartition = partition?.Trim();
    if (string.IsNullOrEmpty(trimmedPartition))
        return storageName;

    // Compose with partition
    var repositorySeparator = np.RepositorySeparator;
    var concretePartition = np.GetConcretePartition(trimmedPartition).Trim();

    return storageName + repositorySeparator + concretePartition;
}
```

### 3. Remove "root" Special Case

**Before:**
```csharp
if (partition is null || partition.Length == 0 ||
    string.Equals(partition, "root", StringComparison.OrdinalIgnoreCase))
    return baseName;
```

**After:**
```csharp
var trimmedPartition = partition?.Trim();
if (string.IsNullOrEmpty(trimmedPartition))
    return storageName;
```

**Rationale:**
- "root" is a valid partition name (e.g., organizational hierarchy: root, regional, branch)
- No justification for special-casing this specific string
- Null or empty (after trim) is sufficient for "no partition" semantics

### 4. Trim All Components Before Composition

**Trimming Policy:**
- Storage name: Trim after `GetStorageName()` (handles whitespace in `[Storage(Name = "  todos  ")]`)
- Partition name: Trim before passing to `GetConcretePartition()`
- Concrete partition: Trim after `GetConcretePartition()` (defensive)
- Separator: Not trimmed (adapter controls value)

**Why Trim:**
- User typos in attributes shouldn't crash framework
- Whitespace-only partitions should be treated as null
- Consistent handling across all naming components

### 5. Cache Key Without Sentinels

**Before:**
```csharp
partition == "root" ? "name:sqlite:root" : "name:sqlite:{partition}"
```

**After:**
```csharp
string.IsNullOrEmpty(trimmedPartition)
    ? $"name:{provider}"
    : $"name:{provider}:{trimmedPartition}"
```

**Examples:**
- No partition: `"name:sqlite"`
- Partition "archive": `"name:sqlite:archive"`
- Partition "root": `"name:sqlite:root"` (treated as regular partition)
- Partition "__none__": `"name:sqlite:__none__"` (treated as regular partition)

**Rationale:**
- Avoid artificial sentinel values that could collide with user partitions
- Simplest design: empty suffix for no partition
- Users can create any partition name without framework restrictions

## Implementation Strategy: Break and Rebuild

**MANDATORY:** No deprecated code, no legacy paths, no [Obsolete] attributes.

This is a greenfield framework (v0.6.3, pre-1.0). Breaking changes are acceptable and preferred over carrying technical debt.

### Phase 1: Update Interface Hierarchy

**File to MODIFY:**
```
src/Koan.Data.Abstractions/IDataAdapterFactory.cs
```

**Change:**
```csharp
- public interface IDataAdapterFactory
+ public interface IDataAdapterFactory : INamingProvider
```

**Rationale:** Makes naming a mandatory part of the adapter contract. Compile-time enforcement.

### Phase 2: Create INamingProvider Interface

**File to CREATE:**
```
src/Koan.Data.Abstractions/Naming/INamingProvider.cs
```

(Interface defined in Decision section above)

### Phase 3: Update All Adapter Factories

**Files to MODIFY (copy logic from *NamingDefaultsProvider):**

1. **SqliteAdapterFactory.cs** (src/Connectors/Data/Sqlite/)
   - Add: `public string RepositorySeparator => "#";`
   - Add: `public string GetConcretePartition(string partition)` - Copy from SqliteNamingDefaultsProvider.FormatPartitionId
   - Add: `public string GetStorageName(Type entityType, IServiceProvider services)` - Copy convention logic

2. **MongoAdapterFactory.cs** (src/Connectors/Data/Mongo/)
   - Add naming methods - Copy from MongoNamingDefaultsProvider
   - Respect `MongoOptions.CollectionName` override in `GetStorageName()`

3. **PostgresAdapterFactory.cs** (src/Connectors/Data/Postgres/)
   - Add naming methods - Copy from PostgresNamingDefaultsProvider

4. **WeaviateAdapterFactory.cs** (src/Connectors/Data/Vector/Weaviate/)
   - Add: `public string RepositorySeparator => "_";` (GraphQL-compliant)
   - Add: `GetConcretePartition()` - Replace hyphens with underscores
   - Add: `GetStorageName()` - FullNamespace with `_` separator
   - **Consolidates WeaviateNamingDefaultsProvider AND IVectorPartitionMapper logic**

5. **MilvusAdapterFactory.cs** (src/Connectors/Data/Vector/Milvus/)
   - Add naming methods - Copy from MilvusNamingDefaultsProvider

6. **QdrantAdapterFactory.cs** (if exists)
   - Add naming methods

7. **InMemoryAdapterFactory.cs** (src/Connectors/Data/InMemory/)
   - Add naming methods (simple pass-through logic)

### Phase 4: Update Core Infrastructure

**Files to MODIFY:**

1. **StorageNameRegistry.cs** (src/Koan.Data.Core/Configuration/)
   - Update `GetOrCompute()` to use `INamingProvider`
   - Update `BagKey()` to remove "root" special case:
     ```csharp
     string.IsNullOrEmpty(trimmedPartition)
         ? $"name:{provider}"
         : $"name:{provider}:{trimmedPartition}"
     ```
   - Add `GetTargetRepositoryName()` orchestration method with trimming
   - Update `ResolveProvider()` to throw if provider not found (no fallback)

2. **StorageNameResolver.cs** (src/Koan.Data.Abstractions/Naming/)
   - Update `Resolve()` to trim attribute values:
     ```csharp
     if (!string.IsNullOrWhiteSpace(storage?.Name))
         return storage.Name!.Trim();

     if (!string.IsNullOrWhiteSpace(storageName?.Name))
         return storageName.Name!.Trim();
     ```

3. **All KoanAutoRegistrar classes** - NO CHANGES NEEDED
   - Factories already registered as `IDataAdapterFactory`
   - Framework auto-discovers they implement `INamingProvider`
   - Optional: Explicitly register as both interfaces for clarity

### Phase 5: Delete Legacy Code

**Files to DELETE (complete removal):**
```
src/Koan.Data.Abstractions/Naming/INamingDefaultsProvider.cs
src/Connectors/Data/Sqlite/SqliteNamingDefaultsProvider.cs
src/Connectors/Data/Mongo/MongoNamingDefaultsProvider.cs
src/Connectors/Data/Postgres/PostgresNamingDefaultsProvider.cs
src/Connectors/Data/Vector/Weaviate/WeaviateNamingDefaultsProvider.cs
src/Connectors/Data/Vector/Milvus/MilvusNamingDefaultsProvider.cs
src/Connectors/Data/Vector/Qdrant/QdrantNamingDefaultsProvider.cs (if exists)
src/Connectors/Data/Vector/Weaviate/Partition/IVectorPartitionMapper.cs
src/Connectors/Data/Vector/Weaviate/Partition/WeaviatePartitionMapper.cs
```

**Rationale:** Complete removal ensures no confusion. No dangling code, no [Obsolete] attributes.

### Phase 6: Update Documentation

**Files to MODIFY:**
- `docs/decisions/DATA-0018-centralized-naming-registry-and-dx.md` - Reference this ADR
- `docs/decisions/DATA-0077-entity-context-source-adapter-partition-routing.md` - Update partition examples
- `.claude/skills/koan-data-modeling/SKILL.md` - Update partition naming guidance
- `CLAUDE.md` - Update framework utilities section

## Migration Impact

### Breaking Changes

**Interface Changes:**
- `INamingDefaultsProvider` → `INamingProvider` (complete replacement)
- `GetConvention()` → `GetStorageName()` (different signature and semantics)
- `FormatPartitionId()` → `GetConcretePartition()` (renamed for clarity)
- Added `RepositorySeparator` property

**Behavioral Changes:**
- "root" partition is now treated as regular partition (no special case)
- All name components trimmed before composition
- Cache keys changed (no "root" sentinel)

**Deleted Components:**
- `INamingDefaultsProvider` interface
- All `*NamingDefaultsProvider` implementations
- `IVectorPartitionMapper` interface
- `WeaviatePartitionMapper` class

### Affected External Code

**Internal Framework Only:**
- All affected code is within Koan Framework repository
- No external adapters or community packages (v0.6.3 is pre-release)
- No migration path needed for external consumers

**If External Adapters Existed:**
1. Update `KoanAutoRegistrar` to register `INamingProvider` instead of `INamingDefaultsProvider`
2. Implement three methods: `GetStorageName()`, `GetConcretePartition()`, `RepositorySeparator`
3. Remove `GetConvention()`, `FormatPartitionId()` implementations
4. Update imports: `using Koan.Data.Abstractions.Naming;`

## Consequences

### Positive

1. **Clear Separation of Concerns:**
   - Storage naming: `GetStorageName()`
   - Partition formatting: `GetConcretePartition()`
   - Composition control: `RepositorySeparator`
   - Each responsibility isolated and testable

2. **Adapter Flexibility:**
   - Weaviate can use `_` separator for GraphQL compliance
   - SQLite can use `#` separator for table naming
   - Each adapter controls all naming aspects

3. **Simplified Implementation:**
   - Single interface to implement (no dual mechanisms)
   - Clear contract: three methods, three responsibilities
   - Framework handles caching and composition

4. **User-Friendly Partition Naming:**
   - No artificial "root" restriction
   - Users can create any partition name (including "root", "__none__")
   - Whitespace trimming prevents typo issues

5. **Pristine Codebase:**
   - No deprecated code paths
   - No [Obsolete] attributes
   - No legacy interfaces dangling
   - Single canonical approach

6. **Consistent Patterns:**
   - All adapters use same interface
   - Vector providers (Weaviate, Milvus) consistent with data providers
   - No special-case implementations

### Negative

1. **Breaking Changes:**
   - Complete replacement of `INamingDefaultsProvider`
   - All adapters must update simultaneously
   - No gradual migration path

2. **Implementation Effort:**
   - 6+ adapter implementations to rewrite
   - Update all auto-registrars
   - Comprehensive testing required

3. **Cache Invalidation:**
   - Cache key format changes (removes "root" sentinel)
   - Existing cached names invalidated on deployment
   - One-time performance hit on first access

### Mitigation

- **Breaking changes:** Acceptable for v0.6.3 (pre-1.0) greenfield framework
- **Implementation effort:** Single atomic commit, all adapters updated together
- **Cache invalidation:** AggregateBags cache is in-memory, repopulates automatically

## Examples

### Example 1: SQLite - GUID Partition

```csharp
[Storage(Name = "todos")]
public class Todo : Entity<Todo> { }

using (EntityContext.Partition("6caab928-3952-48a1-ac60-b1d2a1245c9e"))
{
    await todo.Save();
}

// Resolution flow:
// 1. GetStorageName(typeof(Todo)) → "todos"
// 2. GetConcretePartition("6caab928-3952-48a1-ac60-b1d2a1245c9e") → "6caab928395248a1ac60b1d2a1245c9e"
// 3. RepositorySeparator → "#"
// 4. Compose → "todos#6caab928395248a1ac60b1d2a1245c9e"
```

### Example 2: Weaviate - GraphQL-Compliant Naming

```csharp
namespace MyApp.Vectors;

public class DocumentEmbedding : Entity<DocumentEmbedding> { }

using (EntityContext.Partition("6caab928-3952-48a1-ac60-b1d2a1245c9e"))
{
    await embedding.Save();
}

// Resolution flow:
// 1. GetStorageName(typeof(DocumentEmbedding)) → "MyApp_Vectors_DocumentEmbedding"
// 2. GetConcretePartition("6caab928-...") → "6caab928_3952_48a1_ac60_b1d2a1245c9e"
// 3. RepositorySeparator → "_"
// 4. Compose → "MyApp_Vectors_DocumentEmbedding_6caab928_3952_48a1_ac60_b1d2a1245c9e"
```

### Example 3: "root" as Regular Partition

```csharp
// Before (DATA-0018): "root" was special-cased
using (EntityContext.Partition("root"))
{
    await todo.Save(); // Routed to "Todo" (no suffix)
}

// After (DATA-0086): "root" is regular partition
using (EntityContext.Partition("root"))
{
    await todo.Save(); // Routes to "Todo#root"
}

// Organizational hierarchy example:
// - root: Head office data
// - regional: Regional branch data
// - branch: Individual branch data
```

### Example 4: Whitespace Trimming

```csharp
// User typo in attribute
[Storage(Name = "  todos  ")]
public class Todo : Entity<Todo> { }

// Partition with whitespace
using (EntityContext.Partition("  archive  "))
{
    await todo.Save();
}

// Resolution flow:
// 1. GetStorageName() → "  todos  "
// 2. Framework trims → "todos"
// 3. Partition trimmed → "archive"
// 4. GetConcretePartition("archive") → "archive"
// 5. Compose → "todos#archive"
// ✅ Framework handles user errors gracefully
```

### Example 5: MongoDB with Adapter Override

```csharp
// Startup configuration
services.Configure<MongoOptions>(opts =>
{
    opts.CollectionName = type => type.Name.ToLowerInvariant() + "s";
});

namespace MyApp.Models;
public class Product : Entity<Product> { }

using (EntityContext.Partition("archive"))
{
    await product.Save();
}

// Resolution flow:
// 1. GetStorageName(typeof(Product)) → "products" (adapter override)
// 2. GetConcretePartition("archive") → "archive" (pass-through)
// 3. RepositorySeparator → "#"
// 4. Compose → "products#archive"
```

## Testing Checklist

### Unit Tests

- [ ] `GetStorageName()` respects precedence (attributes → override → convention)
- [ ] `GetConcretePartition()` handles GUIDs, named partitions, edge cases
- [ ] `RepositorySeparator` returns expected value per adapter
- [ ] Trimming: storage name, partition name, concrete partition
- [ ] Null/empty partition → no separator/partition in result
- [ ] "root" partition → treated as regular partition
- [ ] Whitespace-only partition → treated as null
- [ ] Cache key format (no "root" sentinel)

### Integration Tests

- [ ] SQLite: GUID partition → hyphen-free format
- [ ] Weaviate: GraphQL naming with underscore separator
- [ ] MongoDB: Adapter override with partition
- [ ] Postgres: Schema + table + partition composition
- [ ] Milvus: Collection naming with partition
- [ ] Exception thrown when provider not registered (no fallback)
- [ ] AggregateBags caching works per (entity, provider, partition)
- [ ] EntityContext.Partition() integration

### Regression Tests

- [ ] All existing entity storage tests pass
- [ ] Partition routing tests updated and passing
- [ ] Schema provisioning with partitions works
- [ ] Vector search with partitioned collections works
- [ ] KoanContext service partition operations work

## Alternatives Considered

### Alternative 1: Keep INamingDefaultsProvider, Add Separator

**Approach:** Add `PartitionSeparator` property to existing interface.

**Rejected because:**
- Doesn't solve dual mechanism problem (IVectorPartitionMapper still exists)
- Doesn't address "root" special case
- Doesn't fix mixed responsibilities (convention + partition)
- Incremental fix rather than architectural improvement

### Alternative 2: Separate Storage and Partition Interfaces

**Approach:**
```csharp
public interface IStorageNamingProvider { string GetStorageName(...); }
public interface IPartitionNamingProvider {
    string GetConcretePartition(...);
    string RepositorySeparator { get; }
}
```

**Rejected because:**
- Increases complexity (two interfaces to implement)
- Splits related concerns (naming is cohesive domain)
- Framework needs both anyway (no benefit to separation)
- More registration boilerplate

### Alternative 3: Framework-Defined Separator Per Provider Type

**Approach:** Framework decides separator based on provider category (relational vs vector).

**Rejected because:**
- Inflexible: what if SQL provider wants different separator?
- Framework making adapter-specific decisions (wrong layer)
- Hides adapter concerns from adapter implementation
- User requirement: "adapter should handle partition name on their own"

## References

### Supersedes
- **DATA-0018** - Centralized storage naming registry (extends with unified interface)

### Related
- **DATA-0077** - EntityContext partition routing (uses partition formatting)
- **DATA-0030** - Entity sets routing (legacy terminology)

### Implementation Files

**New:**
- `src/Koan.Data.Abstractions/Naming/INamingProvider.cs`

**Modified:**
- `src/Koan.Data.Abstractions/IDataAdapterFactory.cs` (extend INamingProvider)
- `src/Koan.Data.Core/Configuration/StorageNameRegistry.cs` (use INamingProvider, remove fallback)
- `src/Koan.Data.Abstractions/Naming/StorageNameResolver.cs` (trim attribute values)
- `src/Connectors/Data/Sqlite/SqliteAdapterFactory.cs` (add naming methods)
- `src/Connectors/Data/Mongo/MongoAdapterFactory.cs` (add naming methods)
- `src/Connectors/Data/Postgres/PostgresAdapterFactory.cs` (add naming methods)
- `src/Connectors/Data/Vector/Weaviate/WeaviateAdapterFactory.cs` (add naming methods)
- `src/Connectors/Data/Vector/Milvus/MilvusAdapterFactory.cs` (add naming methods)
- `src/Connectors/Data/InMemory/InMemoryAdapterFactory.cs` (add naming methods)

**Deleted:**
- `src/Koan.Data.Abstractions/Naming/INamingDefaultsProvider.cs`
- `src/Connectors/Data/Sqlite/SqliteNamingDefaultsProvider.cs`
- `src/Connectors/Data/Mongo/MongoNamingDefaultsProvider.cs`
- `src/Connectors/Data/Postgres/PostgresNamingDefaultsProvider.cs`
- `src/Connectors/Data/Vector/Weaviate/WeaviateNamingDefaultsProvider.cs`
- `src/Connectors/Data/Vector/Milvus/MilvusNamingDefaultsProvider.cs`
- `src/Connectors/Data/Vector/Weaviate/Partition/IVectorPartitionMapper.cs`
- `src/Connectors/Data/Vector/Weaviate/Partition/WeaviatePartitionMapper.cs`

## Implementation Guide for Agentic Coding Sessions

### Command: "Fully implement DATA-0086-unified-naming-provider-interface.md"

**Prerequisites:**
- Read ALL `*NamingDefaultsProvider.cs` files BEFORE making changes (preserve logic)
- Verify file paths exist before reading/editing
- Build after each phase to catch compilation errors early
- NO deprecated code, NO [Obsolete] attributes

---

### STEP 1: Create INamingProvider Interface

**File:** `src/Koan.Data.Abstractions/Naming/INamingProvider.cs`

**Action:** CREATE new file with exact content from Decision section above.

**Verification:**
```bash
# Should exist and compile
dotnet build src/Koan.Data.Abstractions/Koan.Data.Abstractions.csproj
```

---

### STEP 2: Update IDataAdapterFactory Interface

**File:** `src/Koan.Data.Abstractions/IDataAdapterFactory.cs`

**Action:** Add interface inheritance

**Search for:**
```csharp
public interface IDataAdapterFactory
{
```

**Replace with:**
```csharp
public interface IDataAdapterFactory : INamingProvider
{
```

**Critical:** Add using directive if not present:
```csharp
using Koan.Data.Abstractions.Naming;
```

**Verification:**
```bash
# Should fail with "does not implement interface member 'INamingProvider.GetStorageName'"
dotnet build src/Koan.Data.Abstractions/Koan.Data.Abstractions.csproj
```
Expected: Compilation errors (adapters don't implement yet). This is correct.

---

### STEP 3: Update Adapter Factories (CRITICAL - PRESERVE LOGIC)

**Order matters:** Do SQLite first (simplest), then others.

#### 3.1 SqliteAdapterFactory

**Files to read FIRST:**
1. `src/Connectors/Data/Sqlite/SqliteNamingDefaultsProvider.cs` (source of logic)
2. `src/Connectors/Data/Sqlite/SqliteAdapterFactory.cs` (destination)

**Action:** Add three members to `SqliteAdapterFactory` class:

```csharp
// Add these members to SqliteAdapterFactory class (COPY logic from SqliteNamingDefaultsProvider)

public string RepositorySeparator => "#";

public string GetStorageName(Type entityType, IServiceProvider services)
{
    var opts = services.GetRequiredService<IOptions<SqliteOptions>>().Value;
    var convention = new StorageNameResolver.Convention(
        opts.NamingStyle,
        opts.Separator,
        NameCasing.AsIs);

    return StorageNameResolver.Resolve(entityType, convention);
}

public string GetConcretePartition(string partition)
{
    // SQLite: Remove hyphens from GUIDs
    if (Guid.TryParse(partition, out var guid))
        return guid.ToString("N");  // N format = no hyphens, lowercase

    // Named partitions: sanitize for SQLite table name compatibility
    return SanitizeForSqlite(partition);
}

private static string SanitizeForSqlite(string partition)
{
    var sanitized = new StringBuilder(partition.Length);
    foreach (var c in partition)
    {
        if (char.IsLetterOrDigit(c) || c == '-' || c == '.' || c == '_')
            sanitized.Append(c);
        else
            sanitized.Append('_');
    }
    return sanitized.ToString();
}
```

**Add using directives:**
```csharp
using System.Text;
using Koan.Data.Abstractions.Naming;
```

**Verification:**
```bash
dotnet build src/Connectors/Data/Sqlite/Koan.Data.Connector.Sqlite.csproj
```

---

#### 3.2 MongoAdapterFactory

**Files to read FIRST:**
1. `src/Connectors/Data/Mongo/MongoNamingDefaultsProvider.cs`
2. `src/Connectors/Data/Mongo/MongoAdapterFactory.cs`

**Action:** Add to `MongoAdapterFactory`:

```csharp
public string RepositorySeparator => "#";

public string GetStorageName(Type entityType, IServiceProvider services)
{
    var opts = services.GetRequiredService<IOptions<MongoOptions>>().Value;

    // Check adapter-level override FIRST (MongoOptions.CollectionName)
    if (opts.CollectionName != null)
    {
        var overrideName = opts.CollectionName(entityType);
        if (!string.IsNullOrWhiteSpace(overrideName))
            return overrideName;
    }

    // Fall back to convention
    var convention = new StorageNameResolver.Convention(
        opts.NamingStyle,
        opts.Separator ?? ".",
        NameCasing.AsIs);

    return StorageNameResolver.Resolve(entityType, convention);
}

public string GetConcretePartition(string partition)
{
    // MongoDB: Pass-through (accepts most UTF-8 strings)
    return partition;
}
```

**Add using:**
```csharp
using Koan.Data.Abstractions.Naming;
```

---

#### 3.3 PostgresAdapterFactory

**Files to read FIRST:**
1. `src/Connectors/Data/Postgres/PostgresNamingDefaultsProvider.cs`
2. `src/Connectors/Data/Postgres/PostgresAdapterFactory.cs`

**Action:** Add to `PostgresAdapterFactory`:

```csharp
public string RepositorySeparator => "#";

public string GetStorageName(Type entityType, IServiceProvider services)
{
    var opts = services.GetRequiredService<IOptions<PostgresOptions>>().Value;
    var convention = new StorageNameResolver.Convention(
        opts.NamingStyle,
        opts.Separator,
        NameCasing.AsIs);

    return StorageNameResolver.Resolve(entityType, convention);
}

public string GetConcretePartition(string partition)
{
    // Postgres: Remove hyphens from GUIDs, lowercase
    if (Guid.TryParse(partition, out var guid))
        return guid.ToString("N");  // N format = no hyphens, lowercase

    // Named partitions: lowercase for Postgres convention
    return partition.ToLowerInvariant();
}
```

---

#### 3.4 WeaviateAdapterFactory (COMPLEX - merges TWO sources)

**Files to read FIRST:**
1. `src/Connectors/Data/Vector/Weaviate/WeaviateNamingDefaultsProvider.cs`
2. `src/Connectors/Data/Vector/Weaviate/Partition/WeaviatePartitionMapper.cs` (merge this logic too)
3. `src/Connectors/Data/Vector/Weaviate/WeaviateAdapterFactory.cs`

**Action:** Add to `WeaviateAdapterFactory`:

```csharp
public string RepositorySeparator => "_";  // GraphQL-compliant (NOT "#")

public string GetStorageName(Type entityType, IServiceProvider services)
{
    // Weaviate class names: GraphQL-compliant (no dots)
    var convention = new StorageNameResolver.Convention(
        StorageNamingStyle.FullNamespace,
        "_",  // Underscore separator (dots invalid in GraphQL)
        NameCasing.AsIs);

    return StorageNameResolver.Resolve(entityType, convention);
}

public string GetConcretePartition(string partition)
{
    // Weaviate: Replace hyphens with underscores for GraphQL compatibility
    if (Guid.TryParse(partition, out var guid))
        return guid.ToString("D").Replace("-", "_");  // D format with hyphens → underscores

    // Named partitions: sanitize for GraphQL identifiers
    return SanitizeForGraphQL(partition);
}

private static string SanitizeForGraphQL(string partition)
{
    var sanitized = new StringBuilder(partition.Length);
    for (int i = 0; i < partition.Length; i++)
    {
        var c = partition[i];
        if (i == 0)
        {
            // First char must be letter or underscore
            if (char.IsLetter(c) || c == '_')
                sanitized.Append(c);
            else
                sanitized.Append('_');
        }
        else
        {
            // Subsequent chars: alphanumeric or underscore
            if (char.IsLetterOrDigit(c) || c == '_')
                sanitized.Append(c);
            else
                sanitized.Append('_');
        }
    }
    return sanitized.ToString();
}
```

**Add using:**
```csharp
using System.Text;
using Koan.Data.Abstractions.Naming;
```

**Critical:** This replaces BOTH `WeaviateNamingDefaultsProvider` AND `IVectorPartitionMapper` logic.

---

#### 3.5 MilvusAdapterFactory

**Files to read FIRST:**
1. `src/Connectors/Data/Vector/Milvus/MilvusNamingDefaultsProvider.cs`
2. `src/Connectors/Data/Vector/Milvus/MilvusAdapterFactory.cs`

**Action:** Add to `MilvusAdapterFactory`:

```csharp
public string RepositorySeparator => "#";

public string GetStorageName(Type entityType, IServiceProvider services)
{
    var convention = new StorageNameResolver.Convention(
        StorageNamingStyle.EntityType,  // EntityType (not FullNamespace)
        "_",
        NameCasing.Lower);  // Lowercase

    return StorageNameResolver.Resolve(entityType, convention);
}

public string GetConcretePartition(string partition)
{
    // Milvus: Lowercase and sanitize
    if (Guid.TryParse(partition, out var guid))
        return guid.ToString("N");  // Lowercase, no hyphens

    // Named partitions: lowercase and remove special characters
    return SanitizeForMilvus(partition);
}

private static string SanitizeForMilvus(string partition)
{
    var sanitized = new StringBuilder(partition.Length);
    foreach (var c in partition.ToLowerInvariant())
    {
        if (char.IsLetterOrDigit(c) || c == '_')
            sanitized.Append(c);
        else
            sanitized.Append('_');
    }
    return sanitized.ToString();
}
```

---

#### 3.6 InMemoryAdapterFactory

**File:** `src/Connectors/Data/InMemory/InMemoryAdapterFactory.cs`

**Action:** Add simple pass-through logic:

```csharp
public string RepositorySeparator => "#";

public string GetStorageName(Type entityType, IServiceProvider services)
{
    // InMemory: Simple entity name
    return entityType.Name;
}

public string GetConcretePartition(string partition)
{
    // InMemory: Pass-through
    return partition;
}
```

---

### STEP 4: Update StorageNameRegistry

**File:** `src/Koan.Data.Core/Configuration/StorageNameRegistry.cs`

#### 4.1 Update CacheKey Method

**Search for:**
```csharp
private static string BagKey(string provider, string? set)
    => set is null || set.Length == 0 || string.Equals(set, "root", StringComparison.OrdinalIgnoreCase)
        ? $"name:{provider}:root"
        : $"name:{provider}:{set}";
```

**Replace with:**
```csharp
private static string CacheKey(string provider, string? partition)
{
    var trimmedPartition = partition?.Trim();

    return string.IsNullOrEmpty(trimmedPartition)
        ? $"name:{provider}"
        : $"name:{provider}:{trimmedPartition}";
}
```

**Changes:**
- Rename `BagKey` → `CacheKey`
- Rename `set` parameter → `partition`
- Remove "root" special case
- Add trimming
- Remove `:root` suffix for null/empty

#### 4.2 Update GetOrCompute Method

**Search for calls to `BagKey`:**
```csharp
var key = BagKey(provider, partition);
```

**Replace with:**
```csharp
var key = CacheKey(provider, partition);
```

**Add new orchestration method** (add to class):

```csharp
/// <summary>
/// Orchestrates full repository name resolution.
/// Composes: [StorageName] or [StorageName][Separator][ConcretePartition]
/// </summary>
private static string GetTargetRepositoryName<TEntity>(
    INamingProvider np,
    string? partition,
    IServiceProvider services)
    where TEntity : class
{
    // Get and trim storage name
    var storageName = np.GetStorageName(typeof(TEntity), services).Trim();

    // Trim and check partition
    var trimmedPartition = partition?.Trim();
    if (string.IsNullOrEmpty(trimmedPartition))
        return storageName;

    // Compose with partition
    var repositorySeparator = np.RepositorySeparator;
    var concretePartition = np.GetConcretePartition(trimmedPartition).Trim();

    return storageName + repositorySeparator + concretePartition;
}
```

**Update lambda in `GetOrCompute`:**

**Search for:**
```csharp
return AggregateBags.GetOrAdd<TEntity, TKey, string>(sp, key, () =>
{
    var namingProvider = ResolveProvider(sp, provider);
    var baseName = namingProvider.GetStorageName(typeof(TEntity), sp);
    // ... existing partition logic
});
```

**Replace with:**
```csharp
return AggregateBags.GetOrAdd<TEntity, TKey, string>(sp, key, () =>
{
    var namingProvider = ResolveProvider(sp, provider);
    return GetTargetRepositoryName<TEntity>(namingProvider, partition, sp);
});
```

#### 4.3 Update ResolveProvider Method

**Search for:**
```csharp
private static INamingProvider ResolveProvider(IServiceProvider sp, string providerKey)
{
    var providers = sp.GetServices<INamingProvider>();
    // ... existing logic with FallbackNamingProvider
}
```

**Replace with:**
```csharp
private static INamingProvider ResolveProvider(IServiceProvider sp, string providerKey)
{
    // Factories implement INamingProvider
    var factories = sp.GetServices<IDataAdapterFactory>();
    var provider = factories.FirstOrDefault(f =>
        string.Equals(f.Provider, providerKey, StringComparison.OrdinalIgnoreCase));

    if (provider == null)
    {
        throw new InvalidOperationException(
            $"No adapter registered for provider '{providerKey}'. " +
            $"Ensure an IDataAdapterFactory implementation is registered for this provider.");
    }

    return provider;
}
```

**Add using:**
```csharp
using Koan.Data.Abstractions;
```

---

### STEP 5: Update StorageNameResolver (Trim Attributes)

**File:** `src/Koan.Data.Abstractions/Naming/StorageNameResolver.cs`

**Method:** `public static string Resolve(Type entityType, Convention defaults)`

**Find attribute checks:**
```csharp
if (!string.IsNullOrWhiteSpace(storage?.Name))
    return storage!.Name!;

if (!string.IsNullOrWhiteSpace(storageName?.Name))
    return storageName!.Name!;
```

**Replace with (ADD .Trim()):**
```csharp
if (!string.IsNullOrWhiteSpace(storage?.Name))
    return storage!.Name!.Trim();

if (!string.IsNullOrWhiteSpace(storageName?.Name))
    return storageName!.Name!.Trim();
```

---

### STEP 6: Delete Legacy Files

**Files to DELETE (verify existence first, ignore if not found):**

```bash
rm src/Koan.Data.Abstractions/Naming/INamingDefaultsProvider.cs
rm src/Connectors/Data/Sqlite/SqliteNamingDefaultsProvider.cs
rm src/Connectors/Data/Mongo/MongoNamingDefaultsProvider.cs
rm src/Connectors/Data/Postgres/PostgresNamingDefaultsProvider.cs
rm src/Connectors/Data/Vector/Weaviate/WeaviateNamingDefaultsProvider.cs
rm src/Connectors/Data/Vector/Milvus/MilvusNamingDefaultsProvider.cs
rm src/Connectors/Data/Vector/Qdrant/QdrantNamingDefaultsProvider.cs  # if exists
rm src/Connectors/Data/Vector/Weaviate/Partition/IVectorPartitionMapper.cs
rm src/Connectors/Data/Vector/Weaviate/Partition/WeaviatePartitionMapper.cs
```

**Cleanup KoanAutoRegistrar files** (search for old registrations):

**Pattern to search for:**
```csharp
services.TryAddEnumerable(ServiceDescriptor.Singleton<INamingDefaultsProvider, *NamingDefaultsProvider>());
```

**Action:** DELETE these lines (factories now auto-implement INamingProvider)

**Files to check:**
- `src/Connectors/Data/Sqlite/Initialization/KoanAutoRegistrar.cs`
- `src/Connectors/Data/Mongo/Initialization/KoanAutoRegistrar.cs`
- `src/Connectors/Data/Postgres/Initialization/KoanAutoRegistrar.cs`
- `src/Connectors/Data/Vector/Weaviate/Initialization/KoanAutoRegistrar.cs`
- `src/Connectors/Data/Vector/Milvus/Initialization/KoanAutoRegistrar.cs`

---

### STEP 7: Build and Verify

**Build entire solution:**
```bash
dotnet build
```

**Expected:** Zero errors, zero warnings (except .NET 10 preview warnings)

**If compilation fails:**
1. Check for missing `using` directives
2. Verify all adapter factories implement all three INamingProvider members
3. Check for typos in method names (`GetStorageName` not `GetStoragename`)
4. Ensure `RepositorySeparator` is a property, not a method

---

### STEP 8: Verification Checklist

Run these checks to ensure correct implementation:

#### 8.1 Interface Check
```csharp
// Verify IDataAdapterFactory extends INamingProvider
var factoryType = typeof(IDataAdapterFactory);
var implementsNaming = typeof(INamingProvider).IsAssignableFrom(factoryType);
Assert.True(implementsNaming);
```

#### 8.2 Factory Implementation Check
```csharp
// Verify all factories implement naming methods
var factories = sp.GetServices<IDataAdapterFactory>();
foreach (var factory in factories)
{
    Assert.NotNull(factory.RepositorySeparator);
    Assert.NotNull(factory.GetStorageName(typeof(Todo), sp));
    Assert.NotNull(factory.GetConcretePartition("test"));
}
```

#### 8.3 Storage Name Resolution Check
```csharp
// Verify SQLite GUID partition formatting
var sqliteFactory = sp.GetServices<IDataAdapterFactory>()
    .First(f => f.Provider == "sqlite");

var partition = "6caab928-3952-48a1-ac60-b1d2a1245c9e";
var formatted = sqliteFactory.GetConcretePartition(partition);
Assert.Equal("6caab928395248a1ac60b1d2a1245c9e", formatted);  // No hyphens
```

#### 8.4 Cache Key Check
```csharp
// Verify cache keys don't have "root" sentinel
var key = StorageNameRegistry.CacheKey("sqlite", null);
Assert.Equal("name:sqlite", key);  // NOT "name:sqlite:root"

var keyWithPartition = StorageNameRegistry.CacheKey("sqlite", "archive");
Assert.Equal("name:sqlite:archive", keyWithPartition);
```

#### 8.5 Legacy Code Removal Check
```bash
# Should return empty (no files found)
find . -name "*NamingDefaultsProvider.cs"
find . -name "IVectorPartitionMapper.cs"
```

---

### Common Errors and Solutions

**Error:** "INamingProvider does not exist"
- **Cause:** Step 1 not completed or build cache issue
- **Fix:** Clean and rebuild: `dotnet clean && dotnet build`

**Error:** "SqliteAdapterFactory does not implement GetStorageName"
- **Cause:** Missing method or wrong signature
- **Fix:** Copy exact signature from INamingProvider interface

**Error:** "Ambiguous reference between INamingProvider and INamingDefaultsProvider"
- **Cause:** Legacy files not deleted
- **Fix:** Complete Step 6 (delete all *NamingDefaultsProvider files)

**Error:** "NullReferenceException in GetConcretePartition"
- **Cause:** Not handling null partition input
- **Fix:** Add null check: `if (string.IsNullOrEmpty(partition)) return partition;`

**Error:** "Test fails - partition name has hyphens"
- **Cause:** Copy-paste error in GetConcretePartition (using ToString("D") instead of ToString("N"))
- **Fix:** Use `guid.ToString("N")` for SQLite/Postgres, `guid.ToString("D").Replace("-", "_")` for Weaviate

---

### Post-Implementation Tasks

After successful build:

1. **Run all tests:**
   ```bash
   dotnet test
   ```

2. **Search for obsolete references:**
   ```bash
   grep -r "INamingDefaultsProvider" src/
   grep -r "IVectorPartitionMapper" src/
   # Should return zero results
   ```

3. **Verify boot report:**
   - Start application
   - Check logs for "Registered naming provider: {provider}"
   - Verify all adapters listed

4. **Test partition operations:**
   ```csharp
   using (EntityContext.Partition("019a6626-a445-7ebf-bf33-65cb10ac0ed6"))
   {
       await todo.Save();
       // Verify storage name contains formatted partition
   }
   ```

---

### Critical Success Criteria

✅ All adapter factories implement INamingProvider
✅ Zero compilation errors
✅ Zero legacy files remaining
✅ Cache keys don't contain "root" sentinel
✅ Trimming applied to all name components
✅ GUID partitions formatted correctly per adapter
✅ All tests pass

## Follow-ups

1. Add telemetry for partition formatting decisions (observability)
2. Consider validation hooks in `INamingProvider` for provider-specific rules
3. Document partition naming best practices per adapter type
4. Add performance benchmarks for trimming overhead
5. Consider cached `GetConcretePartition()` results if sanitization is expensive
6. Update ADR-0018 with reference to this unified approach
