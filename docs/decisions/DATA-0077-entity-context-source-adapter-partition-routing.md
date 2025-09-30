---
id: DATA-0077
slug: DATA-0077-entity-context-source-adapter-partition-routing
domain: DATA
status: Accepted
date: 2025-01-15
title: EntityContext - Unified Source/Adapter/Partition Routing with Dynamic Switching
---

# ADR DATA-0077 - EntityContext: Unified Source/Adapter/Partition Routing

## Context

### Current State and Limitations

The framework currently supports limited routing via `DataSetContext` (AsyncLocal for logical "sets"):

```csharp
// Current: set-based routing only
using (DataSetContext.With("backup"))
{
    await todo.Save(); // Routes to "Todo#backup" storage
}
```

**Limitations:**
1. **Static adapter selection**: Each entity type bound to ONE adapter at bootstrap via `[DataAdapter]` attribute or priority-based default
2. **No dynamic adapter switching**: Cannot route entities to different databases (e.g., Postgres → SQLite) at runtime
3. **No named source support**: Cannot switch between configured database instances (e.g., "analytics" vs "backup" servers)
4. **Terminology confusion**: "set" is ambiguous (could mean "set of records", "settings", etc.)

### Use Cases Requiring Dynamic Routing

1. **Multi-tenant data isolation**: Route tenant data to separate database instances
2. **Hot/cold tier management**: Move old data to archive databases
3. **Data replication**: Copy entities across different storage backends
4. **Analytics pipelines**: Send same entity to operational DB and analytics cluster
5. **Backup/restore operations**: Temporarily route to backup infrastructure
6. **Testing**: Switch adapters without configuration changes

### Named Sources Concept

The framework already supports named sources in configuration (`Koan:Data:Sources:{name}`), but lacks API to leverage them:

```json
{
  "Koan": {
    "Data": {
      "Sources": {
        "Analytics": {
          "Adapter": "sqlserver",
          "ConnectionString": "Server=analytics-db;..."
        },
        "Backup": {
          "Adapter": "postgres",
          "ConnectionString": "Host=backup-db;..."
        }
      }
    }
  }
}
```

## Decision

### 1. Introduce EntityContext - Unified Routing API

Replace `DataSetContext` with `EntityContext` supporting three routing dimensions:

```csharp
public static class EntityContext
{
    public sealed record ContextState(
        string? Source,      // Named configuration (e.g., "analytics", "backup")
        string? Adapter,     // Provider override (e.g., "sqlite", "postgres")
        string? Partition);  // Storage partition suffix (e.g., "archive", "cold")

    public static ContextState? Current { get; }

    // Primary API
    public static IDisposable With(
        string? source = null,
        string? adapter = null,
        string? partition = null);

    // Convenience methods
    public static IDisposable Source(string source);
    public static IDisposable Adapter(string adapter);
    public static IDisposable Partition(string partition);
}
```

### 2. Source XOR Adapter Constraint

**Critical constraint:** Source and Adapter are mutually exclusive. Sources define their own adapter selection.

```csharp
// VALID: Source specifies adapter via configuration
using (EntityContext.Source("analytics"))
{
    await metrics.Save(); // Uses "analytics" source's configured adapter
}

// VALID: Explicit adapter on default source
using (EntityContext.Adapter("sqlite"))
{
    await todo.Save(); // SQLite adapter, default source
}

// INVALID: Both source and adapter
using (EntityContext.With(source: "analytics", adapter: "postgres"))
{
    // Throws InvalidOperationException
}
```

**Enforcement:** Constructor validation throws if both source and adapter are specified.

### 3. Rename "Set" → "Partition"

**Rationale:**
- "Partition" is industry-standard terminology (DynamoDB partitions, SQL Server partitions)
- Clearer semantic intent (storage partition, not "set of records")
- Reduces cognitive overload ("set" is overloaded in C# - HashSet, configuration settings, etc.)

**Breaking change:** All `string set` parameters become `string partition` throughout codebase.

```csharp
// Before
await Todo.All("backup");
await todo.Save("archive");

// After
await Todo.All(partition: "backup");
await todo.Save(partition: "archive");

// Or with EntityContext
using (EntityContext.Partition("archive"))
{
    await todo.Save();
}
```

### 4. Partition Naming Rules

**Pattern:** `^[a-zA-Z][a-zA-Z0-9\-\.]*[a-zA-Z0-9]$|^[a-zA-Z]$`

**Rules:**
- MUST start with a letter (a-z, A-Z)
- MAY contain alphanumeric, hyphen (-), or period (.)
- MUST NOT end with hyphen or period
- Case-sensitive (if adapter supports it)
- Single letter is valid (e.g., "a", "B")

**Examples:**
- ✅ Valid: `archive`, `cold-tier`, `backup.v2`, `A`, `prod-us-east-1`
- ❌ Invalid: `1archive` (starts with digit), `backup-` (ends with hyphen), `test.` (ends with period)

### 5. Adapter Resolution Priority

When neither source nor adapter is explicitly specified:

```
Priority chain (first match wins):
1. EntityContext.Current.Source → use source's configured adapter
2. EntityContext.Current.Adapter → explicit adapter override
3. [DataAdapter] or [SourceAdapter] attribute on entity
4. "Default" source (if configured in Koan:Data:Sources:Default)
5. [AdapterPriority] ranking → highest priority factory
```

**Critical rule:** If entity has `[DataAdapter]` attribute, framework MUST honor it or fail. No silent fallback.

### 6. DataSourceRegistry - Auto-Discovery

New service to discover and register sources at bootstrap:

```csharp
public sealed class DataSourceRegistry
{
    public record SourceDefinition(
        string Name,
        string Adapter,
        string ConnectionString,
        IReadOnlyDictionary<string, string> Settings);

    public void DiscoverFromConfiguration(IConfiguration config);
    public void RegisterSource(SourceDefinition source); // Programmatic registration
    public SourceDefinition? GetSource(string name);
}
```

**Discovery rules:**
- Scans `Koan:Data:Sources:{name}` at bootstrap
- Each source MUST have `Adapter` key (except "Default" source)
- Always ensures "Default" source exists (may have empty adapter → uses priority resolution)
- Connection string resolved via existing `IDataConnectionResolver` priority

**Configuration example:**

```json
{
  "Koan": {
    "Data": {
      "Sources": {
        "Default": {
          "Adapter": "sqlite",
          "ConnectionString": "Data Source=app.db"
        },
        "Analytics": {
          "Adapter": "sqlserver",
          "ConnectionString": "Server=analytics;Database=Metrics;...",
          "MaxPageSize": "500",
          "CommandTimeoutSeconds": "60"
        },
        "Cache": {
          "Adapter": "mongodb",
          "ConnectionString": "mongodb://cache-cluster/sessions"
        }
      }
    }
  }
}
```

### 7. Multi-Dimensional Repository Caching

**Current cache key:** `(EntityType, KeyType)`
**New cache key:** `(EntityType, KeyType, Adapter, Source)`

```csharp
public sealed class DataService : IDataService
{
    private readonly ConcurrentDictionary<CacheKey, object> _cache = new();

    private record CacheKey(
        Type EntityType,
        Type KeyType,
        string Adapter,
        string Source);

    public IDataRepository<TEntity, TKey> GetRepository<TEntity, TKey>()
    {
        var ctx = EntityContext.Current;
        var (adapter, source) = AdapterResolver.ResolveForEntity<TEntity>(sp, sourceRegistry);

        var key = new CacheKey(typeof(TEntity), typeof(TKey), adapter, source);

        if (_cache.TryGetValue(key, out var repo))
            return (IDataRepository<TEntity, TKey>)repo;

        // Create and cache repository for this combination
        var factory = GetFactory(adapter);
        var newRepo = factory.Create<TEntity, TKey>(sp, source);
        _cache[key] = newRepo;
        return newRepo;
    }
}
```

**Implications:**
- Repository instances multiplied by (adapters × sources) instead of just entity types
- Acceptable trade-off: Only creates repositories actually used at runtime
- Each repository is long-lived (same lifecycle as default repositories)

### 8. IDataAdapterFactory - Source Parameter

```csharp
public interface IDataAdapterFactory
{
    bool CanHandle(string provider);

    /// <summary>
    /// Create repository for entity with source context.
    /// Source determines connection string and adapter-specific settings.
    /// </summary>
    IDataRepository<TEntity, TKey> Create<TEntity, TKey>(
        IServiceProvider sp,
        string source = "Default")
        where TEntity : class, IEntity<TKey>
        where TKey : notnull;
}
```

**Factory implementation pattern:**

```csharp
public class SqliteAdapterFactory : IDataAdapterFactory
{
    public IDataRepository<TEntity, TKey> Create<TEntity, TKey>(
        IServiceProvider sp,
        string source = "Default")
    {
        var config = sp.GetRequiredService<IConfiguration>();
        var sourceRegistry = sp.GetRequiredService<DataSourceRegistry>();

        // Resolve connection string from source
        var connectionString = ResolveConnectionForSource(config, sourceRegistry, source);

        // Create source-specific options
        var baseOpts = sp.GetRequiredService<IOptions<SqliteOptions>>().Value;
        var sourceOpts = CloneWithConnection(baseOpts, connectionString);

        return new SqliteRepository<TEntity, TKey>(sp, sourceOpts, ...);
    }
}
```

### 9. Provisioning with Error Caching

Repositories auto-provision storage on first operation per (adapter, source) combination:

```csharp
public sealed class EntitySchemaGuard<TEntity, TKey>
{
    private readonly ConcurrentDictionary<string, ProvisionState> _states = new();

    private record ProvisionState(
        bool IsProvisioned,
        DateTime? ProvisionedAt,
        ProvisionError? Error);

    private record ProvisionError(
        string Message,
        DateTime FailedAt,
        int AttemptCount);

    public async Task EnsureProvisionedAsync(
        IDataRepository<TEntity, TKey> repo,
        string adapter,
        string source)
    {
        var key = $"{adapter}:{source}";

        if (_states.TryGetValue(key, out var state))
        {
            if (state.IsProvisioned) return;

            if (state.Error != null)
            {
                // Allow retry after 5 minutes
                var elapsed = DateTime.UtcNow - state.Error.FailedAt;
                if (elapsed < TimeSpan.FromMinutes(5))
                {
                    throw new ProvisioningException(
                        $"Provisioning failed for {typeof(TEntity).Name} on {key}. " +
                        $"Retry in {(TimeSpan.FromMinutes(5) - elapsed):mm\\:ss}");
                }
            }
        }

        try
        {
            await repo.ProvisionAsync();
            _states[key] = new(true, DateTime.UtcNow, null);
        }
        catch (Exception ex)
        {
            var attemptCount = state?.Error?.AttemptCount ?? 0;
            var error = new ProvisionError(ex.Message, DateTime.UtcNow, attemptCount + 1);
            _states[key] = new(false, null, error);
            throw new ProvisioningException($"Provisioning failed (attempt #{attemptCount + 1})", ex);
        }
    }

    // Static helper for manual retry
    public static void ClearProvisioningError<TEntity, TKey>(string? source = null, string? adapter = null)
    {
        var guard = AppHost.Current.GetRequiredService<EntitySchemaGuard<TEntity, TKey>>();
        var key = source ?? adapter ?? "default";
        guard._states.TryRemove(key, out _);
    }
}
```

**Error handling strategy:**
- Cache failures for 5 minutes to prevent retry stampede
- Track attempt count for monitoring
- Expose static clear helper for manual retry after fixing underlying issues

### 10. Direct API Alignment

Update Direct API to support source XOR adapter pattern:

```csharp
public interface IDirectDataService
{
    IDirectSession Direct(string? source = null, string? adapter = null);
}

// Usage
var session = dataService.Direct(source: "analytics");
var session = dataService.Direct(adapter: "postgres");

// Invalid - throws
var session = dataService.Direct(source: "analytics", adapter: "postgres");
```

## Implementation Outline

### Phase 1: Foundation (No Breaking Changes)
1. Create `EntityContext.cs` with validation
2. Create `PartitionNameValidator.cs`
3. Create `DataSourceRegistry.cs` with auto-discovery
4. Create `AdapterResolver.cs` with priority logic
5. Update `IDataAdapterFactory` interface (add source parameter)
6. Update all adapter factories to implement new signature

### Phase 2: Core Integration
7. Update `DataService` with multi-dimensional caching
8. Update `StorageNameRegistry` to read `EntityContext.Current.Partition`
9. Update `EntitySchemaGuard` with provisioning error caching
10. Update `Direct` API for source/adapter routing

### Phase 3: Breaking Changes (Single Atomic Commit)
11. Global search/replace: `string set` → `string partition` in all signatures
12. Global search/replace: `DataSetContext` → `EntityContext.Partition`
13. Delete `DataSetContext.cs`
14. Update all tests
15. Update `CLAUDE.md` and documentation

### Files Requiring Changes

**DELETE:**
- `Koan.Data.Core/DataSetContext.cs`

**CREATE:**
- `Koan.Data.Core/EntityContext.cs`
- `Koan.Data.Core/PartitionNameValidator.cs`
- `Koan.Data.Core/DataSourceRegistry.cs`
- `Koan.Data.Core/AdapterResolver.cs`

**MODIFY (set → partition):**
- `Koan.Data.Core/Data.cs`
- `Koan.Data.Core/Model/Entity.cs`
- `Koan.Data.Core/AggregateExtensions.cs`
- `Koan.Data.Core/DataQueryOptions.cs`
- `Koan.Data.Core/EntitySetMoveExtensions.cs`
- `Koan.Data.Core/Configuration/StorageNameRegistry.cs`
- All test files

**MODIFY (logic updates):**
- `Koan.Data.Core/DataService.cs`
- `Koan.Data.Core/IDataService.cs`
- `Koan.Data.Abstractions/IDataAdapterFactory.cs`
- `Koan.Data.Direct/DirectDataService.cs`
- All connector adapter factories

## Usage Examples

### Example 1: Source-Based Routing

```csharp
// Configuration defines "analytics" source with SQL Server
using (EntityContext.Source("analytics"))
{
    // All operations route to analytics database
    var metrics = await Metric.All();

    foreach (var metric in metrics.Where(m => m.IsStale))
    {
        metric.Archive();
        await metric.Save(); // Saves to analytics SQL Server
    }
}
```

### Example 2: Adapter Override

```csharp
// Force SQLite adapter for testing
using (EntityContext.Adapter("sqlite"))
{
    var testData = GenerateTestData();
    await testData.Save(); // Goes to SQLite regardless of entity config
}
```

### Example 3: Partition Routing

```csharp
// Route to cold storage partition
using (EntityContext.Partition("archive"))
{
    var oldTodos = await Todo.Query(t => t.CompletedAt < cutoffDate);
    await oldTodos.Save(); // Saves to "Todo#archive" storage
}
```

### Example 4: Combined Routing

```csharp
// Analytics source with cold partition
using (EntityContext.With(source: "analytics", partition: "historical"))
{
    await oldMetrics.Save(); // Routes to analytics DB, "Metric#historical" collection
}
```

### Example 5: Nested Context (Replacement)

```csharp
using (EntityContext.Source("primary"))
{
    // All operations use primary source
    await data.Save();

    using (EntityContext.Adapter("sqlite"))
    {
        // REPLACES context: now using SQLite adapter, NOT primary source
        await testData.Save();
    }

    // Back to primary source
    await moreData.Save();
}
```

### Example 6: Runtime Source Registration

```csharp
// In Startup
services.Configure<DataSourceRegistry>(registry =>
{
    registry.RegisterSource(new SourceDefinition(
        Name: "redis-cache",
        Adapter: "redis",
        ConnectionString: "localhost:6379",
        Settings: new Dictionary<string, string>
        {
            ["DefaultDatabase"] = "2",
            ["ConnectTimeout"] = "5000"
        }));
});

// In application code
using (EntityContext.Source("redis-cache"))
{
    await session.Save(); // Routes to Redis cache
}
```

## Consequences

### Positive

1. **Dynamic adapter switching**: Runtime routing between different database technologies
2. **Named source support**: Leverage configuration-defined database instances
3. **Unified routing API**: Single context for all routing dimensions
4. **Clear terminology**: "Partition" is industry-standard and semantically precise
5. **Backward compatibility path**: Breaking changes contained to single commit
6. **Explicit constraints**: Source XOR adapter enforced at API level
7. **Error resilience**: Provisioning failures cached to prevent stampede
8. **Multi-tenant ready**: Foundation for tenant-scoped data isolation

### Negative

1. **Breaking changes**: Complete removal of DataSetContext and "set" terminology
2. **Cache growth**: Repository cache grows O(entity types × adapters × sources)
3. **Complexity**: Three-dimensional routing increases cognitive load
4. **Migration effort**: All existing code using sets must update

### Mitigation

- **Cache growth**: Only creates combinations actually used at runtime
- **Complexity**: Clear documentation and examples for common patterns
- **Migration**: Comprehensive grep/replace checklist provided

## Context for Future Coding Sessions

### Key Architectural Invariants

1. **Source XOR Adapter**: Never allow both simultaneously - sources define their own adapters
2. **Context replacement**: Nested `EntityContext.With()` calls REPLACE, not merge
3. **Partition validation**: Always validate partition names before caching
4. **Provisioning idempotency**: Each (entity, adapter, source) combination provisions exactly once
5. **Repository lifecycle**: Same as default repositories (long-lived, cached)

### Common Debugging Scenarios

**Scenario: "Entity not routing to expected database"**

Check priority chain:
1. Is `EntityContext.Current` set? If so, does source exist in registry?
2. Does entity have `[DataAdapter]` attribute? Framework MUST honor or fail.
3. Is "Default" source configured? Check `DataSourceRegistry.GetSource("Default")`
4. Verify adapter factory priority with `[ProviderPriority]`

**Scenario: "Provisioning fails repeatedly"**

1. Check `EntitySchemaGuard<T,K>._states` for cached errors
2. Use `EntitySchemaGuard<T,K>.ClearProvisioningError(source, adapter)` to force retry
3. Verify connection string resolution via `IDataConnectionResolver`
4. Check adapter's `ProvisionAsync()` implementation

**Scenario: "Partition name validation fails"**

Verify regex: `^[a-zA-Z][a-zA-Z0-9\-\.]*[a-zA-Z0-9]$|^[a-zA-Z]$`
- Must start with letter
- Cannot end with `-` or `.`
- Single letter is valid exception

### Testing Checklist

- [ ] Source routing with configured sources
- [ ] Adapter override without source
- [ ] Partition routing with various names
- [ ] Source XOR adapter validation (should throw)
- [ ] Partition name validation (valid/invalid cases)
- [ ] Nested context replacement behavior
- [ ] Provisioning error caching and retry
- [ ] Multi-dimensional repository caching
- [ ] Adapter resolution priority chain
- [ ] Direct API source/adapter routing

### Migration Checklist

For each codebase migration:

1. **Global replacements:**
   - `DataSetContext.With` → `EntityContext.Partition`
   - `string set,` → `string partition,`
   - `string set)` → `string partition)`
   - `set: ` → `partition: ` (named parameters)

2. **Manual updates:**
   - Configuration keys: `Koan:Data:Sources:{name}`
   - Attribute usage: `[SourceAdapter("provider")]` preferred over `[DataAdapter]`
   - Test assertions referencing "set" terminology

3. **Verification:**
   - All tests passing with partition terminology
   - No references to `DataSetContext` remain
   - All adapter factories implement new signature
   - Bootstrap logs show source discovery

## References

### Supersedes
- **DATA-0030** - Entity Sets (Logical Storage Routing) - terminology replaced
- **DATA-0062** - Instance Save(set) as first-class - API replaced

### Related
- **DATA-0018** - Centralized naming registry and DX
- **DATA-0049** - Direct commands API
- **DATA-0075** - Entity schema guard and provisioning reset

### Implementation
- `Koan.Data.Core.EntityContext`
- `Koan.Data.Core.DataSourceRegistry`
- `Koan.Data.Core.AdapterResolver`
- `Koan.Data.Core.PartitionNameValidator`
- `Koan.Data.Core.DataService`
- `Koan.Data.Abstractions.IDataAdapterFactory`

## Follow-ups

1. Add telemetry for adapter/source routing decisions (observability)
2. Consider typed `PartitionKey` wrapper to reduce magic strings (similar to previous `DataSetKey` proposal)
3. Explore partition strategies beyond storage suffix (e.g., discriminator column mode)
4. Add source health checks and automatic failover for HA scenarios
5. Document multi-tenant patterns using source-based isolation
6. Add metrics for repository cache hit rates per (adapter, source) combination
