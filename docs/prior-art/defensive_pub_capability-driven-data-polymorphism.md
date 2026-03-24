# Defensive Publication: Capability-Driven Data Polymorphism with Runtime Detection and Graceful Fallback

| Field | Value |
|---|---|
| **Title** | Capability-Driven Data Polymorphism with Runtime Detection and Graceful Fallback |
| **Inventor** | Leo Botinelly (Leonardo Milson Botinelly Soares) |
| **Date of Disclosure** | 2026-03-24 |
| **Framework** | Koan Framework v0.6.3 (.NET, target net10.0) |
| **Repository** | github.com/koan-framework (private) |
| **Classification** | Software Architecture -- Data Access Abstraction -- Multi-Provider Polymorphism |
| **Status** | Published as prior art to prevent future patent claims on the described techniques |

---

## 1. Problem Statement

Modern applications increasingly require interaction with heterogeneous data stores. A single application may persist relational data in PostgreSQL, cache ephemeral state in Redis, store documents in MongoDB, and index embeddings in a vector database. Each store offers fundamentally different query semantics: relational databases support SQL and LINQ-translatable expression trees; document stores support rich native query languages but vary in LINQ fidelity; key-value stores typically support only primary-key lookup; vector stores support similarity search over high-dimensional embeddings. No single query interface captures all of these modalities without either (a) reducing all providers to the lowest-common-denominator feature set, or (b) requiring callers to write provider-specific code.

Existing approaches to this problem fall into two categories, both unsatisfactory. The first category -- exemplified by Entity Framework Core -- defines a uniform interface (e.g., `DbSet<T>` with full IQueryable support) and requires every provider to implement the entire surface. Providers that cannot natively fulfill certain operations either throw `NotSupportedException` at runtime with no structured recovery path, or silently evaluate queries client-side with catastrophic performance implications. The second category -- exemplified by Dapper and raw ADO.NET -- provides no abstraction at all, requiring the caller to know which provider is active and write provider-specific query logic.

Neither approach allows application code to be written once, target any data store, and automatically leverage the best available capability of whichever provider is active at runtime, with well-defined degradation paths when a capability is absent. The invention described herein solves this problem through a combination of Interface Segregation Principle (ISP) capability interfaces, runtime interface-presence detection (C# `is` pattern matching), a mediating facade that advertises and bridges capabilities, and a static generic facade that provides fallback logic at each capability boundary.

The system further extends the pattern with flags-based capability enumerations (`QueryCapabilities`, `WriteCapabilities`) that allow callers to introspect provider features before invoking operations, enabling capability-aware UI construction, query planner selection, and diagnostic reporting without any provider-specific code.

---

## 2. Prior Art Summary

### 2.1 Entity Framework Core (Microsoft)

Entity Framework Core defines a single `DbSet<T>` interface exposing `IQueryable<T>`. All providers -- relational and non-relational -- must implement the full LINQ query surface. When a provider cannot translate an expression to native query language, EF Core either throws at runtime or performs client-side evaluation (a behavior that was silent in EF Core 2.x and became an explicit opt-in exception in EF Core 3.0+). There is no structured mechanism for a provider to declare "I support LINQ but not raw SQL" or "I support pagination natively but not LINQ predicates." The interface is monolithic. All providers implement the same surface; variance in capability is expressed only through runtime failure, not through interface presence.

### 2.2 Spring Data (VMware/Pivotal)

Spring Data uses a static class hierarchy: `CrudRepository` provides basic CRUD, `PagingAndSortingRepository` extends it with pagination, and `JpaRepository` extends further with JPA-specific operations. This hierarchy is determined at compile time by which interface the repository declaration extends. There is no runtime detection mechanism: if a caller programs against `CrudRepository`, pagination is statically unavailable regardless of whether the underlying store supports it. If the caller programs against `PagingAndSortingRepository`, all providers must implement pagination even if they cannot do so natively. The hierarchy does not support optional, independently varying capabilities (e.g., "LINQ but not string queries" versus "string queries but not LINQ").

### 2.3 Dapper (StackExchange)

Dapper provides raw SQL execution with efficient object mapping. It offers no abstraction layer: the caller must know the target database, construct provider-specific SQL, and manage connection lifecycle. There is no concept of capability detection, automatic fallback, or multi-provider polymorphism. Switching from one database to another requires rewriting all query code.

### 2.4 Mongoose / Sequelize / Prisma (JavaScript ORMs)

JavaScript ORMs are typically single-provider: Mongoose targets MongoDB exclusively; Sequelize targets relational databases. Prisma offers multi-provider support but exposes a single query API that all providers must fully implement. None provides runtime capability detection with automatic fallback.

### 2.5 Repository Pattern (General)

The classic Repository Pattern (Fowler, "Patterns of Enterprise Application Architecture") defines a single collection-like interface for data access. It does not address the problem of heterogeneous capability sets across providers, runtime detection of those capabilities, or automatic fallback when a capability is absent.

### What Is Missing in All Prior Art

No prior system combines:
1. Independently implementable capability interfaces (ISP decomposition of data access)
2. Runtime detection of capability presence via interface type checking (not configuration flags)
3. Automatic fallback paths in a mediating facade when a capability is absent
4. Flags-based capability enumerations for pre-invocation introspection
5. A factory/adapter pattern that selects the provider at runtime with priority-ranked resolution
6. Cross-cutting concern injection (identity generation, timestamp management, schema health) that preserves the capability surface of the inner repository

---

## 3. Detailed Description

### 3.1 Architecture Overview

The system comprises four cooperating layers:

1. **Capability Interfaces** (`Koan.Data.Abstractions`): A set of independently implementable interfaces, each representing a discrete data access capability.
2. **Adapter Implementations** (`Connectors/Data/*`): Per-provider repository classes that implement whichever subset of capability interfaces the provider natively supports.
3. **Repository Facade** (`Koan.Data.Core.RepositoryFacade<TEntity, TKey>`): An internal mediating wrapper that accepts any `IDataRepository<TEntity, TKey>`, probes it at construction time for capability interfaces, and bridges each capability to the inner repository -- or provides a fallback.
4. **Static Data Facade** (`Koan.Data.Core.Data<TEntity, TKey>`): A public static generic class that resolves the repository for an entity type, detects capabilities at call time, and provides caller-facing fallback logic for paginated, LINQ, string-query, and instruction-execution scenarios.

### 3.2 Capability Interface Decomposition

The base contract is `IDataRepository<TEntity, TKey>`, defining CRUD operations that every adapter must implement:

```csharp
public interface IDataRepository<TEntity, TKey> where TEntity : IEntity<TKey>
{
    Task<TEntity?> Get(TKey id, CancellationToken ct = default);
    Task<IReadOnlyList<TEntity?>> GetMany(IEnumerable<TKey> ids, CancellationToken ct = default);
    Task<IReadOnlyList<TEntity>> Query(object? query, CancellationToken ct = default);
    Task<CountResult> Count(CountRequest<TEntity> request, CancellationToken ct = default);
    Task<TEntity> Upsert(TEntity model, CancellationToken ct = default);
    Task<bool> Delete(TKey id, CancellationToken ct = default);
    Task<int> UpsertMany(IEnumerable<TEntity> models, CancellationToken ct = default);
    Task<int> DeleteMany(IEnumerable<TKey> ids, CancellationToken ct = default);
    Task<int> DeleteAll(CancellationToken ct = default);
    Task<long> RemoveAll(RemoveStrategy strategy, CancellationToken ct = default);
    IBatchSet<TEntity, TKey> CreateBatch();
}
```

Optional capability interfaces are **not** sub-interfaces of `IDataRepository`. They are independently implemented:

```csharp
// Optional: LINQ expression tree query support
public interface ILinqQueryRepository<TEntity, TKey> where TEntity : IEntity<TKey>
{
    Task<IReadOnlyList<TEntity>> Query(
        Expression<Func<TEntity, bool>> predicate, CancellationToken ct = default);
    Task<IReadOnlyList<TEntity>> Query(
        Expression<Func<TEntity, bool>> predicate, DataQueryOptions? options,
        CancellationToken ct = default);
}

// Optional: LINQ with DataQueryOptions support (pagination, sorting, projection)
public interface ILinqQueryRepositoryWithOptions<TEntity, TKey>
    : ILinqQueryRepository<TEntity, TKey>
    where TEntity : IEntity<TKey> where TKey : notnull
{
    new Task<IReadOnlyList<TEntity>> Query(
        Expression<Func<TEntity, bool>> predicate, DataQueryOptions? options,
        CancellationToken ct = default);
}

// Optional: Raw string query support (SQL, Cypher, MQL, etc.)
public interface IStringQueryRepository<TEntity, TKey> where TEntity : IEntity<TKey>
{
    Task<IReadOnlyList<TEntity>> Query(string query, CancellationToken ct = default);
    Task<IReadOnlyList<TEntity>> Query(
        string query, DataQueryOptions? options, CancellationToken ct = default);
}

// Optional: String query with parameterized execution
public interface IStringQueryRepositoryWithOptions<TEntity, TKey>
    : IStringQueryRepository<TEntity, TKey>
    where TEntity : IEntity<TKey> where TKey : notnull
{
    new Task<IReadOnlyList<TEntity>> Query(
        string query, DataQueryOptions? options, CancellationToken ct = default);
    Task<IReadOnlyList<TEntity>> Query(
        string query, object? parameters, DataQueryOptions? options,
        CancellationToken ct = default);
}

// Optional: Server-side paginated query with total count
public interface IPagedRepository<TEntity, TKey> where TEntity : IEntity<TKey>
{
    Task<PagedRepositoryResult<TEntity>> QueryPage(
        object? query, DataQueryOptions options, CancellationToken ct = default);
}

// Optional: Extended CRUD with DataQueryOptions
public interface IDataRepositoryWithOptions<TEntity, TKey>
    : IDataRepository<TEntity, TKey>
    where TEntity : IEntity<TKey> where TKey : notnull
{
    Task<IReadOnlyList<TEntity>> Query(
        object? query, DataQueryOptions? options, CancellationToken ct = default);
}

// Optional: Instruction execution (adapter-specific operations)
public interface IInstructionExecutor<TEntity> where TEntity : class
{
    Task<TResult> ExecuteAsync<TResult>(
        Instruction instruction, CancellationToken ct = default);
}

// Optional: Schema health monitoring
public interface ISchemaHealthContributor<TEntity, TKey>
{
    Task EnsureHealthy(CancellationToken ct);
    void InvalidateHealth();
}

// Optional: Storage optimization (e.g., GUID-to-native-UUID conversion)
public interface IOptimizedDataRepository<TEntity, TKey>
    : IDataRepository<TEntity, TKey>
    where TEntity : class, IEntity<TKey> where TKey : notnull
{
    StorageOptimizationInfo OptimizationInfo { get; }
    bool IsOptimizationEnabled => OptimizationInfo.IsOptimized;
}

// Optional: Bulk operation markers
public interface IBulkUpsert<TKey> where TKey : notnull { }
public interface IBulkDelete<TKey> where TKey : notnull { }
```

### 3.3 Flags-Based Capability Enumerations

In addition to interface-presence detection, the system provides flags enumerations that adapters populate to allow pre-invocation introspection:

```csharp
[Flags]
public enum QueryCapabilities
{
    None         = 0,
    String       = 1 << 0,   // Raw string query support
    Linq         = 1 << 1,   // LINQ expression tree support
    FastCount    = 1 << 2,   // O(1) count (e.g., table statistics)
    OptimizedCount = 1 << 3  // Approximate count (e.g., pg_class.reltuples)
}

[Flags]
public enum WriteCapabilities
{
    None        = 0,
    BulkUpsert  = 1 << 0,
    BulkDelete  = 1 << 1,
    AtomicBatch = 1 << 2,
    FastRemove  = 1 << 3
}

public interface IQueryCapabilities  { QueryCapabilities Capabilities { get; } }
public interface IWriteCapabilities  { WriteCapabilities Writes { get; } }
```

Adapters implement these interfaces and return their native capabilities. The `RepositoryFacade` propagates them:

```csharp
_caps = inner is IQueryCapabilities qc ? qc.Capabilities : QueryCapabilities.None;
_writeCaps = inner is IWriteCapabilities wc ? wc.Writes : WriteCapabilities.None;
```

The static facade exposes them:

```csharp
public static IQueryCapabilities QueryCaps
    => Repo as IQueryCapabilities ?? new Caps(QueryCapabilities.None);
public static IWriteCapabilities WriteCaps
    => Repo as IWriteCapabilities ?? new WriteCapsImpl(WriteCapabilities.None);
```

This allows callers to check `Data<Todo, Guid>.QueryCaps.Capabilities.HasFlag(QueryCapabilities.Linq)` before deciding whether to send a LINQ predicate or load-all-and-filter.

### 3.4 Adapter Capability Profiles

Each adapter implements only the capability interfaces its underlying store natively supports:

| Adapter | Base CRUD | ILinqQueryRepository | IStringQueryRepository | IPagedRepository | IQueryCapabilities | IWriteCapabilities | IInstructionExecutor |
|---|---|---|---|---|---|---|---|
| PostgreSQL | Yes | Yes | Yes (SQL) | No | Yes | Yes | Yes |
| SQL Server | Yes | Yes | Yes (SQL) | No | Yes | Yes | Yes |
| SQLite | Yes | Yes | Yes (SQL) | No | Yes | Yes | Yes |
| MongoDB | Yes | Yes | No | No | Yes | Yes | Yes |
| Redis | Yes | Yes (in-memory) | No | No | Yes | Yes | No |
| JSON File | Yes | Yes (in-memory) | No | No | Yes | Yes | No |
| Couchbase | Yes | Varies | Varies | No | Yes | Yes | Varies |
| InMemory | Yes | Yes | No | No | Yes | Yes | No |

This table is determined at compile time by which interfaces each adapter class declaration includes. For example, `PostgresRepository<TEntity, TKey>` declares:

```csharp
internal sealed class PostgresRepository<TEntity, TKey> :
    IDataRepository<TEntity, TKey>,
    ILinqQueryRepository<TEntity, TKey>,
    IStringQueryRepository<TEntity, TKey>,
    ILinqQueryRepositoryWithOptions<TEntity, TKey>,
    IStringQueryRepositoryWithOptions<TEntity, TKey>,
    IQueryCapabilities,
    IWriteCapabilities,
    IInstructionExecutor<TEntity>
```

While `RedisRepository<TEntity, TKey>` declares:

```csharp
internal sealed class RedisRepository<TEntity, TKey> :
    IDataRepository<TEntity, TKey>,
    ILinqQueryRepository<TEntity, TKey>,
    ILinqQueryRepositoryWithOptions<TEntity, TKey>,
    IQueryCapabilities,
    IWriteCapabilities
```

Redis implements `ILinqQueryRepository` because it deserializes all values into memory and can apply LINQ predicates client-side. It does not implement `IStringQueryRepository` because Redis has no SQL-like query language applicable to generic entity storage. It does not implement `IInstructionExecutor` because the instruction protocol targets adapters with richer native command sets.

### 3.5 Runtime Detection in the Repository Facade

`RepositoryFacade<TEntity, TKey>` wraps any `IDataRepository<TEntity, TKey>` and implements all capability interfaces. At each method call, it probes the inner repository for the relevant capability interface using C# pattern matching (`is` operator):

**LINQ Query Detection:**
```csharp
public async Task<IReadOnlyList<TEntity>> Query(
    Expression<Func<TEntity, bool>> predicate, CancellationToken ct = default)
{
    await Guard(ct);
    if (_inner is ILinqQueryRepository<TEntity, TKey> linq)
        return await linq.Query(predicate, ct);
    throw new NotSupportedException(
        "LINQ queries are not supported by this repository.");
}
```

**LINQ with Options -- Tiered Fallback:**
```csharp
public async Task<IReadOnlyList<TEntity>> Query(
    Expression<Func<TEntity, bool>> predicate, DataQueryOptions? options,
    CancellationToken ct = default)
{
    await Guard(ct);
    if (_inner is ILinqQueryRepositoryWithOptions<TEntity, TKey> linq)
        return await linq.Query(predicate, options, ct);
    if (_inner is ILinqQueryRepository<TEntity, TKey> linqb)
        return await linqb.Query(predicate, ct);  // Fallback: ignore options
    throw new NotSupportedException(
        "LINQ queries are not supported by this repository.");
}
```

**Query with DataQueryOptions -- Transparent Degradation:**
```csharp
public async Task<IReadOnlyList<TEntity>> Query(
    object? query, DataQueryOptions? options, CancellationToken ct = default)
{
    await Guard(ct);
    if (_inner is IDataRepositoryWithOptions<TEntity, TKey> with)
        return await with.Query(query, options, ct);
    return await _inner.Query(query, ct);  // Fallback: ignore options
}
```

**String Query Detection:**
```csharp
public async Task<IReadOnlyList<TEntity>> Query(
    string query, CancellationToken ct = default)
{
    await Guard(ct);
    if (_inner is IStringQueryRepository<TEntity, TKey> raw)
        return await raw.Query(query, ct);
    throw new NotSupportedException(
        "String queries are not supported by this repository.");
}
```

**Instruction Execution Detection:**
```csharp
public async Task<TResult> ExecuteAsync<TResult>(
    Instruction instruction, CancellationToken ct = default)
{
    await Guard(ct);
    if (_inner is IInstructionExecutor<TEntity> exec)
        return await exec.ExecuteAsync<TResult>(instruction, ct);
    throw new NotSupportedException(
        $"Repository for {typeof(TEntity).Name} does not support " +
        $"instruction '{instruction.Name}'.");
}
```

**DeleteAll -- Instruction Fast-Path with Enumeration Fallback:**
```csharp
public async Task<int> DeleteAll(CancellationToken ct = default)
{
    await Guard(ct);
    if (_inner is IInstructionExecutor<TEntity> exec)
    {
        try
        {
            return await exec.ExecuteAsync<int>(
                new Instruction(DataInstructions.Clear), ct);
        }
        catch (NotSupportedException) { /* fall back */ }
    }
    var all = await _inner.Query(null, ct);
    var ids = all.Select(e => e.Id);
    return await _inner.DeleteMany(ids, ct);
}
```

**Schema Health -- Transparent Pass-Through:**
```csharp
public Task EnsureHealthy(CancellationToken ct)
{
    if (_inner is ISchemaHealthContributor<TEntity, TKey> contributor)
        return contributor.EnsureHealthy(ct);
    return Task.CompletedTask;  // No-op if not supported
}
```

### 3.6 Runtime Detection in the Static Data Facade

The `Data<TEntity, TKey>` static class provides additional fallback logic at a higher level. This is where the most complex multi-tier degradation occurs:

**Paginated Query with Multi-Tier Fallback:**
```csharp
// Tier 1: Use IPagedRepository if available (server-side pagination with count)
if (hasPagination && repo is IPagedRepository<TEntity, TKey> pagedRepo)
{
    var repoResult = await pagedRepo.QueryPage(query, normalizedOptions, ct);
    return new QueryResult<TEntity> { /* ... server-side result ... */ };
}

// Tier 2: Use IDataRepositoryWithOptions if available (server-side query with options)
if (repo is IDataRepositoryWithOptions<TEntity, TKey> repoWithOptions)
{
    items = await repoWithOptions.Query(query, normalizedOptions, ct);
    repositoryHandledPagination = hasPagination;
}
else
{
    // Tier 3: Base query, materialize all, apply pagination in memory
    items = await repo.Query(query, ct);
}

// If Tier 3 was used: apply in-memory Skip/Take
if (!repositoryHandledPagination)
{
    var skip = Math.Max(page - 1, 0) * pageSize;
    window = items.Skip(skip).Take(pageSize).ToList();
}
```

**Count with Fallback:**
```csharp
private static async Task<CountOutcome> CountInternal(...)
{
    try
    {
        var result = await repo.Count(request, ct);
        return new CountOutcome(result.Value, result.IsEstimate);
    }
    catch (NotSupportedException)
    {
        var fallbackItems = await LoadItemsForFallback(repo, request, ct);
        return new CountOutcome(fallbackItems.Count, false);
    }
}
```

**LoadItemsForFallback -- Multi-Tier Query Selection:**
```csharp
private static async Task<IReadOnlyList<TEntity>> LoadItemsForFallback(
    IDataRepository<TEntity, TKey> repo, CountRequest<TEntity> request,
    CancellationToken ct)
{
    if (repo is IDataRepositoryWithOptions<TEntity, TKey> repoWithOptions
        && options is not null)
        return await repoWithOptions.Query(payload, options, ct);
    if (request.Predicate is not null
        && repo is ILinqQueryRepository<TEntity, TKey> linq)
        return await linq.Query(request.Predicate, ct);
    if (request.RawQuery is not null
        && repo is IStringQueryRepository<TEntity, TKey> str)
        return await str.Query(request.RawQuery, ct);
    return await repo.Query(payload, ct);
}
```

**Streaming with Capability-Aware Batching:**
```csharp
public static async IAsyncEnumerable<TEntity> QueryStream(
    string query, int? batchSize = null, ...)
{
    if (Repo is IStringQueryRepositoryWithOptions<TEntity, TKey> srepoOpts)
    {
        // Tier 1: Paginated streaming via options-aware string query
        int page = 1;
        while (true)
        {
            var batch = await srepoOpts.Query(query,
                new DataQueryOptions(page, size), ct);
            if (batch.Count == 0) yield break;
            foreach (var item in batch) yield return item;
            if (batch.Count < size) yield break;
            page++;
        }
    }
    else if (Repo is IStringQueryRepository<TEntity, TKey> srepo)
    {
        // Tier 2: Materialize all and yield
        var all = await srepo.Query(query, ct);
        foreach (var item in all) yield return item;
    }
    else
    {
        throw new NotSupportedException(
            "String queries are not supported by this repository.");
    }
}
```

**FirstPage with Fallback:**
```csharp
public static async Task<IReadOnlyList<TEntity>> FirstPage(
    int size, CancellationToken ct = default)
{
    if (Repo is IDataRepositoryWithOptions<TEntity, TKey> repoOpts)
        return await repoOpts.Query(null, new DataQueryOptions(1, size), ct);
    var all = await Repo.Query(null, ct);
    return all.Take(size).ToList();
}
```

### 3.7 Adapter Factory and Resolution

The `IDataAdapterFactory` interface defines the contract for provider plugins:

```csharp
public interface IDataAdapterFactory : INamingProvider
{
    bool CanHandle(string provider);
    IDataRepository<TEntity, TKey> Create<TEntity, TKey>(
        IServiceProvider sp, string source = "Default")
        where TEntity : class, IEntity<TKey> where TKey : notnull;
}
```

The `DataService` resolves the correct factory at runtime using a priority chain defined in `AdapterResolver`:

1. **Ambient context source** (`EntityContext.Current.Source`) -- uses source's configured adapter
2. **Ambient context adapter** (`EntityContext.Current.Adapter`) -- explicit adapter override
3. **Entity attribute** (`[DataAdapter("postgres")]`) -- entity-level configuration
4. **Default source configuration** (`Koan:Data:Sources:Default`) -- framework default
5. **Provider priority ranking** (`[ProviderPriority]` attribute) -- highest priority registered factory

Once resolved, the `DataService` wraps the raw adapter repository in a `RepositoryFacade` and caches the result per `(EntityType, KeyType, Adapter, Source)` tuple:

```csharp
var repo = factory.Create<TEntity, TKey>(sp, source);
var facade = new RepositoryFacade<TEntity, TKey>(repo, manager, guard);
var decorated = ApplyDecorators(typeof(TEntity), typeof(TKey), facade, sp);
_cache[key] = decorated;
```

### 3.8 Cross-Cutting Concern Preservation

The `RepositoryFacade` adds cross-cutting behaviors (identity generation via `IAggregateIdentityManager`, timestamp auto-update via `TimestampPropertyBag`, schema health enforcement via `EntitySchemaGuard`) while preserving the capability surface of the inner repository. Every facade method checks the inner repository for the relevant capability interface before delegating. The decorator chain (`ApplyDecorators`) further extends this principle, allowing additional behaviors (audit logging, CQRS split, etc.) to be layered without losing capability detection.

### 3.9 Patch Operation Fallback

The `Data<T,K>.Patch()` method demonstrates a two-tier fallback for adapter-specific instruction execution:

```csharp
// Tier 1: Try adapter-native patch via instruction execution
if (repo is IInstructionExecutor<TEntity> exec)
{
    try
    {
        return await exec.ExecuteAsync<TEntity?>(
            new Instruction(DataInstructions.Patch, request), ct);
    }
    catch (NotSupportedException) { /* fall back */ }
}
// Tier 2: Read-modify-upsert (works on any adapter)
var current = await repo.Get(request.Id, ct);
if (current is null) return null;
var applicator = PatchApplicators.Create<TEntity, TKey>(...);
applicator.Apply(current);
return await repo.Upsert(current, ct);
```

---

## 4. Claims-Style Disclosure

The following numbered items describe the novel aspects of this system. They are disclosed as prior art to prevent patent claims, not as patent claims themselves.

**Disclosure 1.** A data access system in which a base repository interface defines CRUD operations that all data adapters must implement, and a plurality of optional capability interfaces -- each defining a discrete data access modality (LINQ expression tree queries, raw string queries, server-side pagination, parameterized queries, instruction execution, schema health monitoring, storage optimization, bulk operations) -- are independently implementable by adapters without inheritance from the base interface, such that each adapter implements only the capability interfaces corresponding to features its underlying data store natively supports.

**Disclosure 2.** A repository facade class that wraps any implementation of the base repository interface, implements all capability interfaces, and at each method invocation probes the wrapped inner repository for the relevant capability interface using runtime type checking (C# `is` pattern matching), delegating to the inner repository's native implementation when the capability is present and providing a defined fallback behavior when the capability is absent, wherein the fallback behavior varies by capability: (a) for LINQ queries with options, falling back to LINQ queries without options; (b) for query options, falling back to base query ignoring options; (c) for string queries, throwing a structured `NotSupportedException`; (d) for DeleteAll, attempting instruction execution first, then enumerating IDs and batch-deleting; (e) for schema health monitoring, performing a no-op when unsupported.

**Disclosure 3.** A static generic data facade class (`Data<TEntity, TKey>`) that resolves the active repository for an entity type from a dependency injection container, performs multi-tier capability detection at each public method call, and implements graduated fallback logic: (a) for paginated queries, first attempting `IPagedRepository` (server-side count + page), then `IDataRepositoryWithOptions` (server-side query with options), then base query with in-memory `Skip`/`Take`; (b) for count operations, first attempting native `Count()`, then materializing a fallback query and returning `Count`; (c) for streaming queries, first attempting paginated batching via `IStringQueryRepositoryWithOptions`, then materializing all via `IStringQueryRepository`, then throwing `NotSupportedException`; (d) for patch operations, first attempting adapter-native instruction execution, then performing read-modify-upsert on any adapter.

**Disclosure 4.** Flags-based capability enumeration types (`QueryCapabilities` with flags String, Linq, FastCount, OptimizedCount; `WriteCapabilities` with flags BulkUpsert, BulkDelete, AtomicBatch, FastRemove) exposed through dedicated interfaces (`IQueryCapabilities`, `IWriteCapabilities`) that adapters implement to advertise their native capabilities, propagated through the repository facade to the static data facade, enabling callers to introspect provider capabilities before invoking operations -- without casting, configuration lookup, or provider-specific code.

**Disclosure 5.** An adapter factory pattern in which multiple `IDataAdapterFactory` implementations are registered in a dependency injection container, each declaring a provider name and priority ranking via a `[ProviderPriority]` attribute, and an adapter resolver selects the appropriate factory at runtime through a five-level priority chain: (1) ambient entity context source, (2) ambient entity context adapter, (3) entity-level `[DataAdapter]` attribute, (4) default source configuration, (5) highest-priority registered factory -- such that the same application code works across all providers without modification.

**Disclosure 6.** A repository facade that adds cross-cutting concerns (automatic identity generation, timestamp management, schema health enforcement) while preserving the capability surface of the inner repository, achieved by implementing all capability interfaces and delegating each to the inner repository only when the inner repository implements the corresponding interface, ensuring that capability detection remains accurate through the facade and decorator layers.

**Disclosure 7.** A `CountRequest<TEntity>` dispatch mechanism that carries a polymorphic query payload (LINQ predicate, raw string, or provider-native query object) and a count strategy enum (`Exact`, `Optimized`), where the `LoadItemsForFallback` method selects the best available query path by testing the repository for `IDataRepositoryWithOptions`, then `ILinqQueryRepository`, then `IStringQueryRepository`, then falling back to base `Query()` -- materializing items only when the native count operation throws `NotSupportedException`.

**Disclosure 8.** A partition-scoped data access mechanism in which the `Data<TEntity, TKey>` facade provides partition-qualified overloads for all operations (Get, GetMany, Query, Upsert, Delete, Count), using an ambient `EntityContext` with `IDisposable` scope management, and partition operations participate in the same capability detection and fallback logic as unpartitioned operations, including LINQ fallback for partition-scoped deletes:

```csharp
if (Repo is ILinqQueryRepository<TEntity, TKey> linq)
{
    var items = await linq.Query(predicate, ct);
    return await Repo.DeleteMany(items.Select(e => e.Id), ct);
}
else
{
    var all = await Repo.Query(null, ct);
    var filtered = all.AsQueryable().Where(predicate).ToList();
    return await Repo.DeleteMany(filtered.Select(e => e.Id), ct);
}
```

**Disclosure 9.** A `DataService` that caches resolved and decorated repositories per `(EntityType, KeyType, Adapter, Source)` tuple using a `ConcurrentDictionary`, ensuring that capability detection, facade wrapping, and decorator application occur only once per entity-adapter-source combination for the lifetime of the service, and that the cached decorated repository preserves all capability interfaces through the decorator chain.

**Disclosure 10.** A batch operation facade (`RepositoryFacade.BatchFacade`) that queues add, update, delete, and mutation-by-ID operations, applies identity management to all upserts, resolves mutations by loading current entity state and applying a caller-provided `Action<TEntity>`, and delegates final execution to the inner repository's native batch implementation -- thereby enabling adapter-specific batch semantics (transactions, accurate counts) while providing a uniform batch API across all providers.

---

## 5. Implementation Evidence

The system described above is fully implemented in the Koan Framework v0.6.3 codebase with the following source files:

| Component | File Path |
|---|---|
| Base repository interface | `src/Koan.Data.Abstractions/IDataRepository.cs` |
| LINQ query interface | `src/Koan.Data.Abstractions/ILinqQueryRepository.cs` |
| LINQ query with options | `src/Koan.Data.Abstractions/ILinqQueryRepositoryWithOptions.cs` |
| String query interface | `src/Koan.Data.Abstractions/IStringQueryRepository.cs` |
| String query with options | `src/Koan.Data.Abstractions/IStringQueryRepositoryWithOptions.cs` |
| Paged repository interface | `src/Koan.Data.Abstractions/IPagedRepository.cs` |
| Query options interface | `src/Koan.Data.Abstractions/IDataRepositoryWithOptions.cs` |
| Query capabilities enum | `src/Koan.Data.Abstractions/QueryCapabilities.cs` |
| Write capabilities enum | `src/Koan.Data.Abstractions/WriteCapabilities.cs` |
| Query capabilities interface | `src/Koan.Data.Abstractions/IQueryCapabilities.cs` |
| Write capabilities interface | `src/Koan.Data.Abstractions/IWriteCapabilities.cs` |
| Instruction executor interface | `src/Koan.Data.Abstractions/Instructions/IInstructionExecutor.cs` |
| Bulk operation markers | `src/Koan.Data.Abstractions/IBulkUpsert.cs`, `IBulkDelete.cs` |
| Adapter factory interface | `src/Koan.Data.Abstractions/IDataAdapterFactory.cs` |
| Data adapter attribute | `src/Koan.Data.Abstractions/DataAdapterAttribute.cs` |
| Repository facade | `src/Koan.Data.Core/RepositoryFacade.cs` |
| Static data facade | `src/Koan.Data.Core/Data.cs` |
| Data service | `src/Koan.Data.Core/DataService.cs` |
| Adapter resolver | `src/Koan.Data.Core/AdapterResolver.cs` |
| Optimized repository interface | `src/Koan.Data.Core/Optimization/IOptimizedDataRepository.cs` |
| Schema health contributor | `src/Koan.Data.Core/Schema/ISchemaHealthContributor.cs` |
| PostgreSQL adapter | `src/Connectors/Data/Postgres/PostgresRepository.cs` |
| SQL Server adapter | `src/Connectors/Data/SqlServer/SqlServerRepository.cs` |
| SQLite adapter | `src/Connectors/Data/Sqlite/SqliteRepository.cs` |
| MongoDB adapter | `src/Connectors/Data/Mongo/MongoRepository.cs` |
| Redis adapter | `src/Connectors/Data/Redis/RedisRepository.cs` |
| JSON file adapter | `src/Connectors/Data/Json/JsonRepository.cs` |
| Couchbase adapter | `src/Connectors/Data/Couchbase/CouchbaseRepository.cs` |
| InMemory adapter | `src/Connectors/Data/InMemory/InMemoryAdapterFactory.cs` |

All source files are part of the Koan Framework Git repository with commit history dating to initial development. The framework targets .NET 10.0 (`net10.0`).

---

## 6. Publication Notice

This document is a **defensive publication** under the defensive prior art doctrine. Its purpose is to establish prior art and prevent any party from obtaining patent protection on the techniques described herein. By publishing this disclosure:

1. The inventor asserts that the described system was conceived, implemented, and in use as of the disclosure date (2026-03-24).
2. This publication is intended to be available as prior art under 35 U.S.C. 102(a)(1) and analogous provisions of international patent law (EPC Art. 54, PCT Art. 33).
3. The inventor does not claim patent rights on the described techniques. This publication is made to ensure freedom to operate for all practitioners.
4. Any patent application filed after this publication date that claims the described techniques -- or obvious variants thereof -- should be refused on grounds of anticipation or obviousness in view of this disclosure.

This document may be freely cited, distributed, and archived by patent offices, IP databases (e.g., IP.com, the Defensive Patent License database), prior art clearinghouses, and any interested party for the purpose of defeating patent claims on the described techniques.

---

## Appendix A: Antagonist Review Cycle

### Round 1 -- Antagonist Analysis

**Role**: Patent examiner attempting to find grounds for patentability despite this disclosure.

**Attack Vector 1 -- "The ISP decomposition is obvious":**
A patent examiner might argue that applying the Interface Segregation Principle to repository interfaces is an obvious design choice, and that the novelty lies only in the specific set of interfaces chosen, which is an implementation detail not worth protecting.

*Counter*: The disclosure does not claim novelty in ISP alone. The novelty is in the *combination* of (1) ISP-decomposed capability interfaces, (2) runtime interface-presence detection via type checking, (3) multi-tier automatic fallback in a mediating facade, and (4) flags-based capability introspection -- all cooperating within a single data access system that spans heterogeneous providers. No prior system combines all four elements. The specific interfaces are disclosed in detail to ensure the combination is fully anticipated.

**Attack Vector 2 -- "Runtime type checking (is/as) is a standard C# pattern":**
The `is` keyword is a standard language feature. A patent applicant might claim the novelty is in *what* is checked and *what fallback* is executed.

*Counter*: The disclosure describes the complete set of capability interfaces, the complete set of runtime checks, and the complete set of fallback paths. Every check-and-fallback pair is explicitly enumerated. The specific graduated fallback chains (IPagedRepository -> IDataRepositoryWithOptions -> base Query -> in-memory Skip/Take) are described in full detail, leaving no room for a later applicant to claim novelty in the fallback topology.

**Attack Vector 3 -- "The adapter factory priority chain is separate from capability detection":**
An applicant might try to patent the five-level adapter resolution chain independently.

*Counter*: Disclosure 5 explicitly describes the five-level priority chain (ambient context source -> ambient context adapter -> entity attribute -> default source -> factory priority ranking). This is fully anticipated.

**Attack Vector 4 -- "The combination with cross-cutting concerns (identity, timestamps, schema) through the facade is novel":**
An applicant might argue that while individual patterns exist, the combination of capability-preserving cross-cutting concern injection through a facade that also performs runtime capability detection is novel.

*Counter*: Disclosure 6 explicitly addresses this combination. The `RepositoryFacade` adding identity generation, timestamp management, and schema health while preserving all capability interfaces via delegation is fully described.

**Attack Vector 5 -- "Narrow claim: the specific CountRequest dispatch with polymorphic payload":**
An applicant might try to narrowly claim the `CountRequest<TEntity>` carrying a polymorphic query payload with multi-tier fallback for count operations specifically.

*Counter*: Disclosure 7 and the detailed description in Section 3.6 fully anticipate this. The `LoadItemsForFallback` method with its four-tier query selection is explicitly described.

**Verdict Round 1**: No viable attack vectors remain. All identified angles are explicitly anticipated by the disclosures.

### Round 2 -- Variant Exploration

**Variant 1 -- "What if someone uses configuration flags instead of interface presence?":**
A system that checks a `Dictionary<string, bool>` of capability flags rather than using `is` type checking achieves the same result through a different detection mechanism.

*Counter*: This variant is functionally equivalent and would be obvious in view of this disclosure. The disclosure's Detailed Description establishes that the core innovation is *runtime detection of variable capability sets with automatic fallback*, not the specific detection mechanism. A PHOSITA would trivially substitute configuration flags for interface presence. Furthermore, the flags-based enumeration approach (`QueryCapabilities`, `WriteCapabilities`) is already disclosed as a complementary mechanism.

**Variant 2 -- "What if the fallback is configurable rather than hardcoded?":**
A system where fallback behavior is specified via configuration (e.g., "on missing LINQ, use: memory-filter vs. throw vs. delegate-to-secondary-provider") rather than coded in the facade.

*Counter*: This is an obvious extension. The disclosure establishes the pattern of capability detection + fallback. Making the fallback strategy configurable is a routine engineering choice that a PHOSITA would consider. The principle is fully anticipated.

**Variant 3 -- "What if applied to non-data-access domains (e.g., AI model adapters, notification providers)?":**
The same ISP + runtime detection + fallback pattern applied to AI model capabilities (some models support embeddings, some support chat, some support function calling) or notification providers (some support push, some support email).

*Counter*: This is the same pattern applied to a different domain. The disclosure's description is sufficiently general that a PHOSITA would recognize the technique as domain-independent. The Koan Framework's own AI architecture (AI-0021 ADR, referenced in project memory) already applies the same ISP adapter split to AI categories (`IChatAdapter`, `IEmbedAdapter`, `IOcrAdapter`), further establishing generality.

**Verdict Round 2**: All variants are either functionally equivalent or obvious extensions of the disclosed system.

### Round 3 -- Clearance Determination

**Final Assessment**: The disclosure is comprehensive. It covers:

- The complete interface hierarchy with exact type signatures
- Every adapter's capability profile with evidence from source code
- The full runtime detection logic with all `is` checks enumerated
- Every fallback path including multi-tier graduated degradation
- The adapter factory resolution priority chain
- Cross-cutting concern preservation through the facade
- Flags-based capability introspection as a complementary mechanism
- Partition-scoped operations with capability-aware fallback
- Caching of resolved decorated repositories
- Batch operation facade with mutation-by-ID resolution

**Known gaps**: None identified. The disclosure is sufficient to anticipate both the specific implementation and obvious variants.

**CLEARANCE GRANTED**: This defensive publication is ready for archival as prior art.

---

*End of Defensive Publication*
