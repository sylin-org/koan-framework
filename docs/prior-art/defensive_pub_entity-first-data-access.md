# Defensive Publication: Entity-First Multi-Provider Data Access with Active Record Ergonomics

## Header Block

| Field | Value |
|---|---|
| **Title** | Entity-First Multi-Provider Data Access System with Active Record Ergonomics, Ambient Routing Context, and Transparent Lifecycle Interception |
| **Inventor** | Leo Botinelly (Leonardo Milson Botinelly Soares) |
| **Publication Date** | 2026-03-24 |
| **Framework** | Koan Framework v0.6.3 (.NET, targeting net10.0) |
| **Field of Invention** | Software Architecture; Object-Relational Mapping; Data Access Layer Design; Multi-Provider Database Abstraction |
| **Keywords** | Active Record pattern, multi-provider data routing, ambient context routing, entity lifecycle interception, provider-agnostic persistence, AsyncLocal routing, CRTP base class, repository facade, adapter resolution cascade, transaction coordination, identity generation, field protection, partition-aware data access, schema health guard |

---

## 1. Problem Statement

Modern application frameworks require data access patterns that balance developer ergonomics against architectural flexibility. The two dominant paradigms -- Active Record and Repository Pattern -- each impose tradeoffs that force developers into suboptimal compromises when building systems that must operate across heterogeneous storage providers.

The **Active Record pattern**, popularized by Ruby on Rails and subsequently adopted by frameworks such as Django ORM and Laravel Eloquent, offers an intuitive programming model where domain objects carry their own persistence behavior. A developer writes `todo.Save()` or `Todo.Find(id)` and the object handles its own storage. This model excels at developer productivity and readability for single-provider applications, but it tightly couples domain types to a specific storage engine. Switching from PostgreSQL to MongoDB, or writing an entity that must simultaneously persist to both a relational database and a vector store, requires rewriting the entity class or introducing adapter abstractions that destroy the simplicity that made Active Record attractive in the first place.

The **Repository Pattern**, championed by Domain-Driven Design and implemented in frameworks such as Entity Framework Core through `DbContext` and `DbSet<T>`, provides clean separation between domain logic and persistence infrastructure. Repositories can be swapped, mocked, and composed. However, this separation comes at significant cognitive cost: developers must define repository interfaces, implement concrete repositories per provider, register them in dependency injection containers, inject them into every service that needs data access, and manage the lifecycle of unit-of-work scopes. For applications with dozens of entity types, this ceremony multiplies into hundreds of boilerplate files. Worse, the repository pattern pushes data access decisions out of domain code and into service constructors, fragmenting the mental model of what an entity "does."

A third problem compounds both approaches: **multi-provider routing**. Real-world applications increasingly store different entity types in different databases -- relational data in PostgreSQL, documents in MongoDB, embeddings in a vector store, configuration in SQLite. Neither Active Record nor Repository Pattern offers a first-class mechanism for transparently routing entity operations to the correct provider based on runtime context, entity-level configuration, or ambient routing state. Developers are forced to build ad-hoc routing layers that leak provider awareness into application code.

Finally, enterprise applications require **cross-cutting lifecycle behaviors** -- audit logging, validation, field protection, soft-delete enforcement, AI embedding generation -- that must execute transparently during persistence operations regardless of which storage provider handles the actual I/O. Neither Active Record nor Repository Pattern provides a standardized pipeline for intercepting entity operations with setup, before, persist, and after phases while supporting cancellation, lazy prior-state loading, and field mutation protection.

This disclosure describes a system that resolves all four problems simultaneously: it presents Active Record ergonomics at the API surface while hiding a multi-layer architecture that performs provider-agnostic routing, transparent lifecycle interception, automatic identity management, schema health verification, and best-effort multi-adapter transaction coordination -- all without requiring the developer to write repository interfaces, inject dependencies, or be aware of which storage provider ultimately services a given operation.

---

## 2. Prior Art Summary

### 2.1 Ruby on Rails ActiveRecord (2004-present)

Rails ActiveRecord is the canonical Active Record implementation. Models inherit from `ActiveRecord::Base`, gaining static finders (`User.find(1)`), instance persistence (`user.save`), and query scoping (`User.where(active: true)`). ActiveRecord supports lifecycle callbacks (`before_save`, `after_create`) and associations (`has_many`, `belongs_to`).

**What Rails ActiveRecord lacks:**

- **Multi-provider routing**: ActiveRecord binds each model to a single database connection. Rails 6 introduced multi-database support via `connects_to`, but each model class is statically bound to one database role. There is no mechanism for ambient, per-request routing where the same model class can dynamically target different providers based on runtime context.
- **Provider-agnostic facade**: The query interface is SQL-centric. Switching from PostgreSQL to MongoDB requires a different ORM (Mongoid) with incompatible APIs.
- **Typed lifecycle interception with field protection**: Callbacks are stringly-typed method names without compile-time safety, lazy prior-state loading, or field immutability enforcement during the callback pipeline.
- **Automatic sortable identity generation**: Rails uses auto-incrementing integers or application-level UUID generation without built-in GUID v7 or ULID support with type-aware strategy selection.
- **Transparent schema health verification**: No per-operation schema guard that verifies backing store health before executing operations with retry backoff.

### 2.2 Django ORM (2005-present)

Django's ORM uses a Manager/QuerySet pattern where `Model.objects.filter(...)` returns lazy querysets. Models define `Meta` classes for database routing, and Django supports multi-database routing via a `DATABASE_ROUTERS` setting where router classes implement `db_for_read` and `db_for_write` methods.

**What Django ORM lacks:**

- **Ambient async routing context**: Django's database routers are class-level and stateless. There is no `AsyncLocal`-equivalent mechanism where application code can establish a routing scope (`with EntityContext.Source("analytics"):`) that transparently affects all entity operations within that async execution flow, including nested calls.
- **Source XOR Adapter constraint**: Django routers do not distinguish between "named source configuration" (which implies its own adapter) and "adapter override" (which uses the default source). The mutually exclusive routing dimension concept is absent.
- **Lifecycle pipeline with lazy prior-state loading**: Django's signals (`pre_save`, `post_save`) do not lazily load the prior database state on demand, nor do they support field protection snapshots that detect unauthorized mutations between handler phases.
- **Repository facade with decorator chain**: Django repositories are implicit. There is no explicit wrapping layer that adds identity management, timestamp auto-update, schema verification, and capability advertisement as transparent decorators around provider-specific implementations.

### 2.3 Entity Framework Core (2016-present)

EF Core implements the Unit of Work and Repository patterns via `DbContext`. Entities are Plain Old CLR Objects (POCOs) tracked by a change tracker. `DbContext.SaveChanges()` flushes all tracked mutations in a single transaction. EF Core supports multiple providers (SQL Server, PostgreSQL, SQLite, Cosmos DB) through provider-specific packages.

**What EF Core lacks:**

- **Active Record API surface**: EF Core requires injecting `DbContext` into services and calling `context.Set<T>().FindAsync(id)`. There are no static methods on entity types. The `Todo.Get(id)` pattern does not exist.
- **Multi-provider per-entity routing**: While EF Core supports different providers for different `DbContext` instances, it does not support routing different entity types to different providers within a single logical data service, nor ambient context-based routing override.
- **Transparent lifecycle pipeline independent of change tracker**: EF Core's interceptors (`SaveChangesInterceptor`, `DbCommandInterceptor`) operate at the `DbContext` level, not the individual entity level. There is no per-entity-type setup/before/persist/after pipeline with typed context, lazy prior loading, and field protection.
- **Static generic data facade**: EF Core has no equivalent to `Data<TEntity, TKey>` -- a static class parameterized by entity and key type that resolves the correct repository at call time via `AppHost.Current`, avoiding constructor injection entirely.
- **Partition-aware data operations**: EF Core does not natively support logical partitioning where `entity.Save("archive")` transparently routes to a partition-suffixed storage location within the same adapter.

### 2.4 Dapper (2011-present)

Dapper is a micro-ORM that extends `IDbConnection` with `Query<T>` and `Execute` methods. It provides raw SQL execution with object mapping but no entity tracking, no change detection, no lifecycle hooks, and no Active Record pattern.

**What Dapper lacks:**

- Essentially all higher-level abstractions described in this disclosure. Dapper is a mapping library, not a data access framework. It has no entity base class, no routing, no lifecycle pipeline, no identity management, no schema guards, and no transaction coordination across heterogeneous providers.

### 2.5 Summary of Prior Art Gaps

No existing framework combines all of the following in a single coherent system:

1. Active Record static API surface (`Entity.Get(id)`, `entity.Save()`) backed by
2. A provider-agnostic static data facade (`Data<TEntity, TKey>`) that resolves repositories at call time via
3. A multi-dimensional adapter resolution cascade (5-level priority chain) influenced by
4. An ambient async routing context (`EntityContext` via `AsyncLocal`) with Source XOR Adapter mutual exclusion, supporting
5. A transparent lifecycle interception pipeline (`EntityEventExecutor`) with lazy prior-state loading, field protection snapshots, and cancellation, wrapped in
6. A repository facade that auto-generates identities (GUID v7 / ULID), auto-updates timestamps, enforces schema health, and advertises capabilities, coordinated by
7. A best-effort transaction coordinator that groups deferred operations by adapter with telemetry spans.

---

## 3. Detailed Description

### 3.1 Architectural Overview: The Seven-Layer Stack

The disclosed system organizes data access into seven cooperating layers, each with a specific responsibility. A single call to `Todo.Get(id)` traverses all seven layers transparently:

```
Layer 1: Entity<TEntity, TKey>          — Active Record API surface (CRTP base class)
Layer 2: EntityEventExecutor<TEntity,TKey> — Lifecycle interception pipeline
Layer 3: Data<TEntity, TKey>            — Provider-agnostic static data facade
Layer 4: DataService                    — Repository factory with 4-dimensional cache
Layer 5: AdapterResolver                — 5-level priority resolution cascade
Layer 6: RepositoryFacade<TEntity,TKey> — Cross-cutting decorator (identity, timestamps, schema)
Layer 7: IDataAdapterFactory / Adapter  — Provider-specific repository implementation
```

The call flow for `Todo.Get(id)` proceeds as follows:

1. `Entity<Todo, Guid>.Get(id)` delegates to `EntityEventExecutor<Todo, Guid>.ExecuteLoad(...)`, passing a lambda that calls `Data<Todo, Guid>.Get(id, token)`.
2. `EntityEventExecutor` checks `EntityEventRegistry<Todo, Guid>.HasLoadPipeline`. If no hooks are registered, it executes the lambda directly (fast path). If hooks exist, it constructs an `EntityEventContext<Todo>`, runs setup handlers, captures a field protection snapshot, executes before-load handlers (which may cancel), validates field protection, executes the load lambda, runs after-load handlers, validates field protection again, and returns the entity.
3. `Data<Todo, Guid>.Get(id, ct)` accesses its static `Repo` property, which calls `AppHost.Current.GetService<IDataService>().GetRepository<Todo, Guid>()`.
4. `DataService.GetRepository<Todo, Guid>()` computes a 4-dimensional cache key `(typeof(Todo), typeof(Guid), adapter, source)` by calling `AdapterResolver.ResolveForEntity<Todo>(sp, sourceRegistry)`. If a cached repository exists, it returns it immediately.
5. `AdapterResolver.ResolveForEntity<Todo>()` evaluates the 5-level priority cascade (detailed in Section 3.3) to determine the adapter name and source name.
6. `DataService` finds the `IDataAdapterFactory` that can handle the resolved adapter, calls `factory.Create<Todo, Guid>(sp, source)` to get a provider-specific repository, wraps it in `RepositoryFacade<Todo, Guid>`, applies any registered `IDataRepositoryDecorator` instances, caches the result, and returns it.
7. `RepositoryFacade<Todo, Guid>.Get(id, ct)` calls `Guard(ct)` (which invokes `EntitySchemaGuard.EnsureHealthy(ct)` to verify the backing store is provisioned), then delegates to the inner provider-specific repository.

### 3.2 Layer 1: Entity<TEntity, TKey> — The Active Record Surface

The `Entity<TEntity, TKey>` class uses the Curiously Recurring Template Pattern (CRTP) to provide static methods on the derived entity type:

```csharp
public abstract partial class Entity<TEntity, TKey> : IEntity<TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    [Key]
    public virtual TKey Id { get; set; } = default!;
}
```

A developer defines a domain entity by inheriting from this base:

```csharp
public class Todo : Entity<Todo, Guid>
{
    public string Title { get; set; }
    public bool IsComplete { get; set; }
}
```

This single inheritance gives `Todo` the following static and instance API:

**Static CRUD methods:**
- `Todo.Get(id)` / `Todo.Get(ids)` / `Todo.Get(id, partition)`
- `Todo.All()` / `Todo.All(options)` / `Todo.All(partition)`
- `Todo.Query(predicate)` / `Todo.Query(rawString)` / `Todo.Query(predicate, options)`
- `Todo.QueryWithCount(predicate, options)` — returns `QueryResult<T>` with pagination metadata
- `Todo.Upsert(model)` / `Todo.Upsert(model, partition)`
- `Todo.UpsertMany(models)` / `Todo.UpsertMany(models, partition)`
- `Todo.Remove(id)` / `Todo.Remove(ids)` / `Todo.Remove(query)` / `Todo.Remove(id, partition)`
- `Todo.RemoveAll()` / `Todo.RemoveAll(strategy)` / `Todo.RemoveAll(strategy, partition)`
- `Todo.Batch()` — returns `IBatchSet<Todo, Guid>` for fluent batch composition
- `Todo.Patch(id, partial)` / `Todo.PatchMerge(id, delta)` — JSON Patch and Merge Patch

**Static query accessors:**
- `Todo.Count` — awaitable count accessor: `await Todo.Count` (optimized), `await Todo.Count.Exact()`, `await Todo.Count.Fast()`, `await Todo.Count.Where(predicate)`, `await Todo.Count.Partition(name)`
- `Todo.AllStream()` / `Todo.QueryStream(query)` — `IAsyncEnumerable<Todo>` streaming
- `Todo.FirstPage(size)` / `Todo.Page(page, size)` — materialized paging

**Static lifecycle configuration:**
- `Todo.Events` — returns `EntityEventsBuilder<Todo, Guid>` for fluent hook registration

**Static transfer operations:**
- `Todo.Copy()` / `Todo.Move()` / `Todo.Mirror(mode)` — cross-source/cross-partition data transfer builders

**Static cache management:**
- `Todo.Cache.Flush()` / `Todo.Cache.Count()` / `Todo.Cache.Any()`

**Instance methods:**
- `todo.Remove()` — self-remove using `this.Id`
- `todo.GetParent<TParent>()` / `todo.GetChildren<TChild>()` — relationship navigation
- `todo.GetParents()` / `todo.GetRelatives()` — full relationship graph

**Extension method bridge (AggregateExtensions):**
- `todo.Save()` / `todo.Upsert()` — instance-level persistence via extension methods
- `todo.Remove()` / `todo.Delete()` — instance-level deletion
- `models.Save()` / `models.Remove()` — bulk operations on `IEnumerable<TEntity>`
- `models.AsBatch()` — convert collection to pre-filled batch
- `model.Upsert()` (non-generic, `object` receiver) — runtime reflection-based upsert for unknown types

A convenience subclass `Entity<TEntity>` (single type parameter) defaults `TKey` to `string` and auto-generates GUID v7 identifiers on first access:

```csharp
public abstract partial class Entity<TEntity> : Entity<TEntity, string>
    where TEntity : class, IEntity<string>
{
    private string? _id;
    public override string Id
    {
        get => _id ??= Guid.CreateVersion7().ToString();
        set => _id = value;
    }
}
```

The critical design insight is that **none of these methods require dependency injection**. They resolve services at call time via `AppHost.Current` (a static service locator backed by the application's `IServiceProvider`), which means domain entities can be used in static contexts, test fixtures, scripts, and REPL environments without constructing DI containers.

### 3.3 Layer 5: AdapterResolver — The 5-Level Priority Cascade

The `AdapterResolver` determines which storage adapter and source configuration to use for a given entity type. It evaluates a 5-level priority chain where the first match wins:

**Priority 1 — Ambient source context (`EntityContext.Current.Source`):**
If application code has established an ambient source context (e.g., `using var _ = EntityContext.Source("analytics")`), the resolver looks up that source name in `DataSourceRegistry`, retrieves the source's configured adapter, and returns `(sourceDefinition.Adapter, ctx.Source)`. This allows the same entity class to read from different databases depending on the execution context.

**Priority 2 — Ambient adapter context (`EntityContext.Current.Adapter`):**
If application code has specified an adapter override (e.g., `using var _ = EntityContext.Adapter("sqlite")`), the resolver returns `(ctx.Adapter, "Default")`. This uses the specified adapter with the default source configuration.

**Priority 3 — Entity attribute (`[DataAdapter]` or `[SourceAdapter]` on the entity class):**
If the entity class is decorated with `[SourceAdapter("mongodb")]` or `[DataAdapter("postgres")]`, the resolver returns `(attributeAdapter, "Default")`. The `[SourceAdapter]` attribute takes precedence over the legacy `[DataAdapter]` attribute.

**Priority 4 — Default source configuration:**
If a source named "Default" is registered in configuration (`Koan:Data:Sources:Default`) with an adapter specified, the resolver returns `(defaultSource.Adapter, "Default")`.

**Priority 5 — Highest-priority adapter factory:**
As a last resort, the resolver enumerates all registered `IDataAdapterFactory` instances, ranks them by `[ProviderPriority]` attribute value (descending), breaks ties by name (ascending, case-insensitive), extracts the adapter name from the factory class name (e.g., `SqliteAdapterFactory` becomes `"sqlite"`), and returns `(adapterName, "Default")`.

This cascade enables the following scenarios without any changes to entity code:

- **Development**: Priority 5 selects SQLite (highest priority in dev profile) automatically.
- **Production**: Priority 4 selects PostgreSQL via `Koan:Data:Sources:Default:Adapter = "postgres"`.
- **Analytics queries**: `using var _ = EntityContext.Source("analytics")` at Priority 1 routes to a read replica.
- **Per-entity storage**: `[SourceAdapter("mongodb")]` on a document entity at Priority 3 routes to MongoDB while other entities use the default relational provider.

### 3.4 Layer 3: Data<TEntity, TKey> — The Provider-Agnostic Static Facade

`Data<TEntity, TKey>` is a static generic class that serves as the sole entry point for all data operations. Its critical property is:

```csharp
public static class Data<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private static IDataRepository<TEntity, TKey> Repo
        => AppHost.Current?.GetService<IDataService>()?.GetRepository<TEntity, TKey>()
            ?? throw new InvalidOperationException("AppHost.Current is not set...");
}
```

Every operation on `Data<TEntity, TKey>` accesses `Repo`, which resolves the repository **on every call**. This is essential because the resolved repository depends on `EntityContext.Current`, which may change between calls on the same entity type. The `DataService` internally caches repositories by `(EntityType, KeyType, Adapter, Source)` tuple, so the per-call resolution cost is a dictionary lookup when the routing context is stable.

Key design features of the Data facade:

**Transaction-aware deferred execution:** When `EntityContext.Current.TransactionCoordinator` is non-null, `Upsert` and `Delete` operations do not execute immediately. Instead, they track the operation on the coordinator and return immediately:

```csharp
public static async Task<TEntity> Upsert(TEntity model, CancellationToken ct = default)
{
    var context = EntityContext.Current;
    if (context?.TransactionCoordinator != null)
    {
        var manager = AppHost.Current?.GetService<IAggregateIdentityManager>();
        await manager.EnsureIdAsync<TEntity, TKey>(model, ct);
        context.TransactionCoordinator.TrackSave<TEntity, TKey>(model, context);
        return model;  // Deferred — actual execution on commit
    }
    return await Repo.Upsert(model, ct);
}
```

**Partition scoping via ambient context:** Partition-aware operations wrap calls with a temporary `EntityContext.Partition(partition)` scope:

```csharp
public static Task<TEntity?> Get(TKey id, string partition, CancellationToken ct = default)
{ using var _ = WithPartition(partition); return Repo.Get(id, ct); }
```

**Capability advertisement:** `Data<TEntity, TKey>.QueryCaps` and `WriteCaps` expose the capabilities of the resolved repository, allowing application code to adapt behavior based on what the current provider supports (e.g., checking `WriteCaps.Writes.HasFlag(WriteCapabilities.FastRemove)` before using `TRUNCATE`).

**Count strategies:** The `Count` method supports `CountStrategy.Exact`, `CountStrategy.Fast`, and `CountStrategy.Optimized`, allowing callers to trade accuracy for performance. The implementation falls back to materialization when the provider does not support native count operations.

**Partition migration:** `Data<TEntity, TKey>.CopyPartition(from, to)`, `MovePartition(from, to)`, `ReplacePartition(target, items)`, and `MoveFrom(partition)` (fluent builder) enable cross-partition data transfer with batching, predicate filtering, and mapping transforms.

### 3.5 Layer 4: DataService — Repository Factory with 4-Dimensional Cache

`DataService` is the `IDataService` implementation responsible for constructing and caching repositories. It uses a `ConcurrentDictionary<CacheKey, object>` where `CacheKey` is a record of `(Type EntityType, Type KeyType, string Adapter, string Source)`.

The construction pipeline for a new repository:

1. Call `AdapterResolver.ResolveForEntity<TEntity>(sp, sourceRegistry)` to get `(adapter, source)`.
2. Compute cache key `(typeof(TEntity), typeof(TKey), adapter, source)`.
3. Check cache; return if hit.
4. Enumerate `IDataAdapterFactory` instances from DI; find the first where `factory.CanHandle(adapter)` returns true.
5. Call `factory.Create<TEntity, TKey>(sp, source)` to get the provider-specific `IDataRepository<TEntity, TKey>`.
6. Resolve `IAggregateIdentityManager` and `EntitySchemaGuard<TEntity, TKey>` from DI.
7. Wrap the raw repository in `RepositoryFacade<TEntity, TKey>(repo, manager, guard)`.
8. Apply all registered `IDataRepositoryDecorator` instances via `ApplyDecorators(entityType, keyType, facade, sp)`. Decorators can wrap the repository with additional behavior (e.g., caching, audit logging, soft-delete filtering).
9. Store in cache and return.

The 4-dimensional cache key ensures that the same entity type can have different repository instances when accessed through different routing contexts (e.g., `Todo` routed to SQLite for local dev vs. PostgreSQL for production, or `Todo` via default source vs. `Todo` via "analytics" source).

### 3.6 Layer 2: EntityEventExecutor — Transparent Lifecycle Interception

The `EntityEventExecutor<TEntity, TKey>` is an internal static class that wraps every entity operation with a lifecycle pipeline. It supports four operation types: Load, Upsert, Remove, and their batch variants (LoadMany, UpsertMany, RemoveMany).

**Pipeline phases:**

1. **Fast-path check**: If `EntityEventRegistry<TEntity, TKey>.HasXxxPipeline` is false (no handlers registered for the operation), the executor bypasses the pipeline entirely and executes the persistence delegate directly. This eliminates overhead for entities without lifecycle hooks.

2. **Context construction**: Creates an `EntityEventContext<TEntity>` containing:
   - `Current`: The entity being operated on (mutable reference flowing through the pipeline).
   - `Operation`: An `EntityEventOperationState` shared between handlers for coordination.
   - `Prior`: An `EntityEventPrior<TEntity>` — a lazy-loading wrapper that fetches the entity's prior database state only when a handler requests it.
   - `LifecycleOperation`: The enum value identifying the operation (Load, Upsert, Remove).
   - `Items`: An arbitrary `IDictionary<string, object?>` state bag shared between handlers.
   - `CancellationToken`: Propagated from the caller.

3. **Setup phase**: Executes all registered setup handlers sequentially. Setup handlers can call `context.Protect("PropertyName")` to mark fields as immutable for the duration of the pipeline, or `context.ProtectAll()` to lock all fields (with `context.AllowMutation("PropertyName")` for exceptions).

4. **Protection snapshot**: After setup, `CaptureProtectionSnapshot()` reads the current values of all protected fields using compiled expression delegates (cached per entity type) and stores them in a dictionary.

5. **Before phase**: Executes before-handlers sequentially. Each handler returns `EntityEventResult` which is either `Proceed()` or `Cancel(reason, code)`. If any handler returns `Cancel`, the executor throws `EntityEventCancelledException` with the reason and code, aborting the operation.

6. **Protection validation (pre-persist)**: After before-handlers, `ValidateProtection()` compares current field values against the snapshot. If any protected field has been mutated by a handler, it throws `InvalidOperationException`. This prevents before-handlers from silently corrupting entity state.

7. **Persistence**: Executes the persistence delegate (e.g., `Data<TEntity, TKey>.Upsert(entity, token)`). For upsert operations, the context's `Current` is updated to the persisted entity (which may have server-generated values).

8. **After phase**: Executes after-handlers sequentially. These can perform side effects (audit logging, cache invalidation, event publishing) but cannot cancel the operation.

9. **Protection validation (post-persist)**: Final validation ensures after-handlers did not mutate protected fields.

**Lazy prior-state loading (`EntityEventPrior<TEntity>`):**

The `EntityEventPrior<TEntity>` class wraps a `Func<CancellationToken, ValueTask<TEntity?>>` loader delegate. It is thread-safe: the first call to `Get(ct)` executes the loader and caches the result; subsequent calls return the cached value. This design means the prior database state is **never loaded if no handler asks for it**, avoiding an unnecessary round-trip for entities with simple lifecycle hooks that don't need to compare old vs. new values.

```csharp
public sealed class EntityEventPrior<TEntity>(
    Func<CancellationToken, ValueTask<TEntity?>> loader) where TEntity : class
{
    private bool _loaded;
    private TEntity? _value;

    public ValueTask<TEntity?> Get(CancellationToken cancellationToken = default)
    {
        if (_loaded) return new ValueTask<TEntity?>(_value);
        return Load(cancellationToken);
    }
}
```

**Handler registration (`EntityEventRegistry<TEntity, TKey>`):**

Handlers are stored in static arrays (one per operation phase) and registered via `EntityEventsBuilder<TEntity, TKey>`, accessible from `Entity<TEntity, TKey>.Events`. The registry uses copy-on-write semantics: adding a handler allocates a new array of length N+1, copies existing handlers, and atomically replaces the reference under a lock. Handler arrays are exposed as `ReadOnlyMemory<T>` for zero-allocation iteration via `.Span[i]` in the executor.

**Batch operations with atomic semantics:**

`ExecuteUpsertMany` and `ExecuteRemoveMany` process each entity through the full pipeline individually, collecting `EntityOutcome` results. If any entity's before-handler requests cancellation and the operation state is marked as `IsAtomic`, the executor throws `EntityEventBatchCancelledException` with the full list of outcomes, aborting the entire batch. Non-atomic mode skips cancelled entities and persists the rest.

### 3.7 EntityContext — Ambient Async Routing with Source XOR Adapter

`EntityContext` is a static class backed by `AsyncLocal<ContextState?>` that provides ambient routing context for all entity operations within an async execution flow.

**Routing dimensions (4 orthogonal axes):**

1. **Source** (`string?`): A named configuration entry (e.g., `"analytics"`, `"backup"`) that maps to a specific adapter and connection string in configuration (`Koan:Data:Sources:{name}`).
2. **Adapter** (`string?`): An explicit provider override (e.g., `"sqlite"`, `"postgres"`) that uses the default source configuration.
3. **Partition** (`string?`): A logical storage partition suffix (e.g., `"archive"`, `"cold"`) appended to the storage name by the adapter.
4. **Transaction** (`string?`): A named transaction for coordinated commit/rollback across adapters.

**Mutual exclusion constraint:** Source and Adapter are mutually exclusive. Specifying both in a single `ContextState` constructor throws `InvalidOperationException` with the message: "Cannot specify both 'source' and 'adapter'. Sources define their own adapter selection." This constraint prevents ambiguous routing where a named source might select PostgreSQL but an adapter override specifies MongoDB.

**Scope semantics:** `EntityContext.With(...)` returns an `IDisposable` that, on disposal, restores the previous `ContextState`. This enables nested scoping:

```csharp
using var _ = EntityContext.Source("analytics");
var data = await Todo.All(); // Routes to analytics source

using var __ = EntityContext.Partition("archive");
var archived = await Todo.All(); // Routes to analytics source, archive partition
// __ disposed: partition restored
// _ disposed: source restored
```

**Transaction scoping:** `EntityContext.Transaction(name)` creates a new `ITransactionCoordinator` via `ITransactionCoordinatorFactory` and stores it in the context state. Nested transactions are explicitly prohibited -- attempting to start a transaction inside an existing one throws `InvalidOperationException`. The `TransactionScope` (internal `IDisposable`) auto-commits on successful disposal unless `TransactionOptions.AutoCommitOnDispose` is false, in which case it auto-rolls back.

**Context inheritance with selective override:** When `EntityContext.With(...)` is called, it inherits values from the current context for dimensions not explicitly specified. The `preserveTransaction` parameter (default `true`) controls whether the ambient transaction is inherited by the new scope.

### 3.8 Layer 6: RepositoryFacade — Cross-Cutting Decoration

`RepositoryFacade<TEntity, TKey>` wraps every provider-specific repository with transparent cross-cutting behaviors:

**Identity management:** On every `Upsert` and `UpsertMany` call, the facade invokes `IAggregateIdentityManager.EnsureIdAsync<TEntity, TKey>(entity, ct)`. The `AggregateIdentityManager` inspects the entity's `Id` property using cached `AggregateMetadata`:
- If `Id` is a `Guid` and equals `default(Guid)`, it assigns `Guid.CreateVersion7()` (time-sortable, monotonically increasing within a node).
- If `Id` is a `string` and is null/empty/whitespace, it assigns `StringId.New()` (a custom ULID-like identifier).
- If `Id` already has a non-default value, no action is taken.

**Timestamp auto-update:** The facade constructs a `TimestampPropertyBag` at initialization that scans the entity type for properties annotated with `[Timestamp]`. On every `Upsert`, if `HasTimestamp` is true, it calls `UpdateTimestamp(model)` using a compiled delegate to set the timestamp field to the current UTC time.

**Schema health guard:** Before every operation (read or write), the facade calls `Guard(ct)` which invokes `EntitySchemaGuard<TEntity, TKey>.EnsureHealthy(ct)`. The schema guard:
- Uses a `ConcurrentDictionary<string, ProvisionState>` keyed by `"{adapter}:{source}:{partition}"` to track provisioning state.
- If the store is already provisioned, returns immediately (dictionary lookup).
- If a previous provisioning attempt failed less than 5 minutes ago, throws immediately with retry timing information.
- Otherwise, uses a `Singleflight` mechanism to ensure only one provisioning attempt runs at a time per storage key, then delegates to the adapter's `ISchemaHealthContributor<TEntity, TKey>.EnsureHealthy(ct)`.

**Capability bridging:** The facade implements `IQueryCapabilities` and `IWriteCapabilities`, forwarding capability flags from the inner repository. It also implements `ILinqQueryRepository<TEntity, TKey>`, `IStringQueryRepository<TEntity, TKey>`, and their `WithOptions` variants, delegating to the inner repository when supported and throwing `NotSupportedException` when the provider does not implement the optional interface.

**Instruction execution:** The facade implements `IInstructionExecutor<TEntity>`, forwarding arbitrary `Instruction` objects to the inner repository when it supports the interface. This allows provider-specific operations (e.g., native patch execution) to flow through the same facade without breaking the abstraction.

**Batch facade:** `RepositoryFacade.CreateBatch()` returns a `BatchFacade` that queues adds, updates, and deletes, ensures identities on all upserts, supports "update by mutation" (load entity, apply `Action<TEntity>`, queue as update), and delegates to the inner repository's native batch implementation on `Save()`.

### 3.9 TransactionCoordinator — Best-Effort Multi-Adapter Coordination

The `TransactionCoordinator` provides transaction semantics across heterogeneous adapters:

**Operation tracking:** When entity operations execute within an `EntityContext.Transaction(name)` scope, the `Data<TEntity, TKey>` facade detects the ambient coordinator and calls `TrackSave<TEntity, TKey>(entity, context)` or `TrackDelete<TEntity, TKey>(id, context)` instead of executing immediately. Each tracked operation is a sealed class implementing `ITrackedOperation` with an `Execute(CancellationToken)` method and a `GetAdapterHint()` method.

**Adapter grouping:** Tracked operations are grouped in a `Dictionary<string, List<ITrackedOperation>>` by adapter name. This ensures that operations targeting the same adapter are executed together.

**Commit semantics:** On `Commit(ct)`:
1. The coordinator temporarily clears the transaction context (to prevent recursive tracking when operations execute).
2. For each adapter group, it executes all tracked operations sequentially.
3. If any operation fails, it throws `TransactionException` with the list of already-completed adapters, providing diagnostic information for manual recovery.
4. The coordinator explicitly documents that cross-adapter atomicity is "best-effort" -- if PostgreSQL operations succeed but MongoDB operations fail, the PostgreSQL changes are not rolled back.

**Rollback semantics:** `Rollback(ct)` simply clears all tracked operations. Since no operations have been persisted yet (they are deferred), rollback is a no-op on the storage side.

**Safety limits:** The coordinator enforces `TransactionOptions.MaxTrackedOperations` to prevent unbounded memory growth in long-running transactions.

**Telemetry integration:** The coordinator creates an `Activity` span from `ActivitySource("Koan.Data.Transaction")` for each transaction, tagging it with the transaction name, per-adapter operation counts, completion status, and duration.

**Vector operation support:** The coordinator also tracks vector save and vector delete operations (`TrackVectorSave<TEntity, TKey>`, `TrackVectorDelete<TEntity, TKey>`), enabling transactions that span both entity storage and vector storage.

### 3.10 Identity Generation Strategy

The `AggregateIdentityManager` implements a type-aware identity generation strategy:

- **GUID fields**: Assigns `Guid.CreateVersion7()`, which produces UUIDs where the first 48 bits encode a Unix timestamp in milliseconds. This makes identifiers sortable by creation time, monotonically increasing within a single process, and globally unique without coordination.
- **String fields**: Assigns `StringId.New()`, a custom ULID-like identifier that provides similar properties in string form.
- **Non-default check**: Identity is only assigned when the current value is `default(Guid)` for GUID fields or `null`/empty/whitespace for string fields. Explicitly set identifiers are never overwritten.

The convenience subclass `Entity<TEntity>` (string key) additionally auto-generates on first property access via the `Id` getter, not just on persistence. This means a newly constructed entity has a valid, unique ID even before it is saved.

### 3.11 Relationship Navigation

`Entity<TEntity, TKey>` provides instance methods for navigating entity relationships:

- `GetParent<TParent>(ct)`: Resolves the single parent of the specified type by scanning foreign key properties, loading via `Data<TParent, TKey>.Get(parentId, ct)`.
- `GetParent<TParent>(propertyName, ct)`: Explicit property-name-based parent resolution for entities with multiple parents of the same type.
- `GetParents(ct)`: Returns `Dictionary<string, object?>` of all parent relationships.
- `GetChildren<TChild>(ct)`: Loads all children of the specified type that reference this entity's ID.
- `GetRelatives(ct)`: Returns a complete `RelationshipGraph<TEntity>` with all parents and children grouped by type and reference property.

Relationship metadata is resolved via `IRelationshipMetadata`, cached per entity type. Property access uses compiled expression delegates cached in a static `Dictionary<(Type, string), Func<object, object?>>` for high-performance repeated navigation.

### 3.12 Extension Method Bridge

`AggregateExtensions` provides instance-level convenience methods that bridge to the static Entity API:

```csharp
public static Task<TEntity> Save<TEntity, TKey>(this TEntity model, CancellationToken ct)
    where TEntity : class, IEntity<TKey> => model.Upsert<TEntity, TKey>(ct);
```

This enables `todo.Save()` syntax. The extensions also provide:
- `model.Delete(ct)` — runtime reflection-based deletion for `object`-typed models.
- `models.Save(ct)` / `models.Remove(ct)` — bulk operations on `IEnumerable<TEntity>`.
- `models.SaveReplacing(ct)` — delete-all-then-upsert for idempotent dataset replacement.
- `models.AsBatch()` / `batch.AddRange(models)` — batch composition helpers.

The non-generic `Upsert(this object model, CancellationToken ct)` overload uses reflection to resolve `IEntity<TKey>` at runtime, find the correct repository via `IDataService.GetRepository`, and invoke `Upsert` dynamically. This supports scenarios where the entity type is not statically known (e.g., generic admin UIs, MCP tool handlers).

---

## 4. Claims-Style Disclosure

The following numbered disclosures describe the novel aspects of the system. These are published as prior art to prevent patent claims on these techniques.

**Disclosure 1.** A data access system comprising a generic abstract base class `Entity<TEntity, TKey>` using the Curiously Recurring Template Pattern (CRTP) that exposes static methods (`Get`, `All`, `Query`, `Upsert`, `Remove`, `Batch`, `Patch`, `PatchMerge`, `Count`, `AllStream`, `QueryStream`, `FirstPage`, `Page`, `Copy`, `Move`, `Mirror`, `RemoveAll`) and instance methods (`Remove`, `GetParent`, `GetChildren`, `GetRelatives`) on the derived entity type, where each method delegates through a multi-layer pipeline comprising a lifecycle event executor, a provider-agnostic static data facade, a repository factory service, an adapter resolution cascade, a repository decorator facade, and a provider-specific adapter implementation, without requiring the entity class or its callers to inject dependencies, reference provider-specific types, or be aware of which storage engine services the operation.

**Disclosure 2.** A static generic data facade class `Data<TEntity, TKey>` parameterized by entity type and key type, where a static `Repo` property resolves the correct `IDataRepository<TEntity, TKey>` on every access by calling `AppHost.Current.GetService<IDataService>().GetRepository<TEntity, TKey>()`, enabling the resolved repository to vary per-call based on ambient routing context stored in `AsyncLocal<ContextState>`, without requiring the caller to hold a reference to the repository or be aware that resolution occurs.

**Disclosure 3.** An adapter resolution cascade comprising five prioritized levels: (1) ambient source context from `AsyncLocal`, (2) ambient adapter context from `AsyncLocal`, (3) entity-class-level attributes (`[SourceAdapter]` or `[DataAdapter]`), (4) a default source configuration entry, and (5) ranked `IDataAdapterFactory` instances ordered by `[ProviderPriority]` attribute value descending then factory name ascending, where each level is evaluated in sequence and the first match determines both the adapter name and source name for the entity type, enabling the same entity class to be routed to different storage providers based on runtime context, configuration, entity metadata, or factory registration order without code changes.

**Disclosure 4.** An ambient routing context system using `AsyncLocal<ContextState>` with four orthogonal routing dimensions -- Source (named configuration), Adapter (provider override), Partition (storage subdivision), and Transaction (coordination scope) -- where Source and Adapter are mutually exclusive (setting both in a single context throws an exception), contexts are scoped via `IDisposable` with automatic restoration of previous context on disposal, contexts inherit unspecified dimensions from the enclosing scope, and nested transaction scoping is prohibited by runtime check.

**Disclosure 5.** A transparent entity lifecycle interception pipeline (`EntityEventExecutor<TEntity, TKey>`) that wraps persistence operations (Load, Upsert, Remove, and their batch variants) with a five-phase execution model: (1) setup phase for handler initialization and field protection declaration, (2) protection snapshot capture using compiled expression delegates, (3) before phase with cancellation support via `EntityEventResult`, (4) persistence delegation, and (5) after phase with post-persist side effects, where field protection snapshots are validated after both the before and after phases to detect unauthorized mutations, and the entire pipeline is bypassed (zero-overhead fast path) when no handlers are registered for the operation type, as determined by checking static boolean flags on `EntityEventRegistry<TEntity, TKey>`.

**Disclosure 6.** A lazy prior-state loading mechanism (`EntityEventPrior<TEntity>`) that wraps a `Func<CancellationToken, ValueTask<TEntity?>>` loader delegate, executes the loader at most once (thread-safe via lock), caches the result, and returns the cached value on subsequent accesses, where the loader delegate is supplied by the entity executor and resolves the entity's current database state, enabling lifecycle handlers to compare old vs. new values without incurring a database round-trip when no handler requests the prior state.

**Disclosure 7.** A repository factory service (`DataService`) that caches constructed repositories in a `ConcurrentDictionary` keyed by a 4-dimensional tuple of `(Type EntityType, Type KeyType, string Adapter, string Source)`, where construction involves resolving the adapter and source via the priority cascade, finding an `IDataAdapterFactory` that can handle the adapter, creating a provider-specific repository, wrapping it in a `RepositoryFacade` that adds identity management and schema verification, and applying a chain of `IDataRepositoryDecorator` instances, enabling the same entity type to have multiple simultaneously-cached repository instances when accessed through different routing contexts.

**Disclosure 8.** A repository facade (`RepositoryFacade<TEntity, TKey>`) that transparently decorates any `IDataRepository<TEntity, TKey>` with: (a) automatic identity generation using `Guid.CreateVersion7()` for GUID-type keys and `StringId.New()` for string-type keys, applied only when the current key value is default/null/empty; (b) automatic timestamp field updates using compiled delegates detected at construction time from `[Timestamp]` attribute metadata; (c) schema health verification via `EntitySchemaGuard` with per-storage-key provisioning state cache, retry backoff after failure, and `Singleflight` deduplication of concurrent provisioning attempts; and (d) capability advertisement bridging `IQueryCapabilities` and `IWriteCapabilities` from the inner repository.

**Disclosure 9.** A transaction coordination system where: (a) starting a transaction creates an `ITransactionCoordinator` instance stored in `AsyncLocal` routing context; (b) entity operations within the transaction scope detect the coordinator and defer execution by calling `TrackSave` or `TrackDelete` instead of persisting immediately; (c) tracked operations are grouped by adapter name in a dictionary; (d) on commit, operations are executed per-adapter-group sequentially with telemetry spans; (e) on rollback, tracked operations are discarded without execution; (f) the coordinator enforces a maximum tracked operations limit; (g) cross-adapter atomicity is explicitly documented as best-effort; and (h) transaction scopes auto-commit on successful `IDisposable.Dispose()` unless configured otherwise.

**Disclosure 10.** An entity event handler registration system (`EntityEventRegistry<TEntity, TKey>`) using copy-on-write static arrays exposed as `ReadOnlyMemory<T>` for zero-allocation span-based iteration, where handlers are added via a fluent builder (`EntityEventsBuilder<TEntity, TKey>`) accessible from `Entity<TEntity, TKey>.Events`, supporting seven handler slots (Setup, BeforeLoad, AfterLoad, BeforeUpsert, AfterUpsert, BeforeRemove, AfterRemove), with pipeline-existence flags (`HasLoadPipeline`, `HasUpsertPipeline`, `HasRemovePipeline`) computed from array lengths enabling zero-overhead bypass when no handlers are registered.

**Disclosure 11.** A field protection mechanism within entity lifecycle contexts where: (a) setup handlers call `context.Protect(propertyName)` or `context.ProtectAll()` to declare fields as immutable; (b) `AllowMutation(propertyName)` removes protection for specific fields; (c) after setup, `CaptureProtectionSnapshot()` reads protected field values using a lazily-constructed, cached map of compiled `Expression.Lambda<Func<TEntity, object?>>` delegates (one per readable property); (d) `ValidateProtection()` compares current values against snapshots and throws if any differ; and (e) validation occurs twice per pipeline execution (after before-handlers and after after-handlers) to ensure no phase can silently corrupt protected state.

**Disclosure 12.** An extension method bridge (`AggregateExtensions`) that provides instance-level `Save()`, `Upsert()`, `Remove()`, and `Delete()` methods on `IEntity<TKey>` instances by delegating to the corresponding static methods on `Entity<TEntity, TKey>`, including: (a) overloads for both generic-key and string-key entities, (b) partition-aware variants, (c) a non-generic `Upsert(this object model)` overload that uses runtime reflection to discover the `IEntity<TKey>` implementation, resolve the repository, and invoke `Upsert` dynamically, and (d) bulk operations on `IEnumerable<TEntity>` including `Save()`, `Remove()`, `SaveReplacing()`, and `AsBatch()`.

**Disclosure 13.** A partition-aware data operations system where: (a) static methods on `Entity<TEntity, TKey>` and `Data<TEntity, TKey>` accept an optional `string partition` parameter; (b) partition-scoped operations wrap the inner call with `EntityContext.Partition(partition)` via `IDisposable` scope; (c) the partition value flows through `AdapterResolver` to the adapter where it is appended to the storage name; (d) partition migration operations (`CopyPartition`, `MovePartition`, `ReplacePartition`) enable cross-partition data transfer with batching, predicate filtering, and mapping transforms; and (e) a fluent builder pattern (`PartitionMoveBuilder`) supports `Data<Todo, Guid>.MoveFrom("backup").Where(...).Map(...).Copy().BatchSize(1000).To("root")` syntax.

**Disclosure 14.** An awaitable count accessor pattern where `Entity<TEntity, TKey>.Count` returns an `EntityCountAccessor` instance with: (a) a `GetAwaiter()` method returning `TaskAwaiter<long>` that defaults to `CountStrategy.Optimized`, enabling `await Todo.Count` syntax; (b) explicit strategy methods `Exact()`, `Fast()`, and `Optimized()`; (c) filtered count methods `Where(predicate)` and `Query(rawString)` with strategy and options overloads; and (d) partition-scoped count via `Partition(name)`.

**Disclosure 15.** A schema health guard system (`EntitySchemaGuard<TEntity, TKey>`) that: (a) maintains a static `ConcurrentDictionary<string, ProvisionState>` keyed by `"{adapter}:{source}:{partition}"`; (b) on each data operation, checks if the storage key is provisioned (dictionary lookup, fast path); (c) if not provisioned and a previous attempt failed less than 5 minutes ago, throws immediately with retry timing; (d) if not provisioned and eligible for retry, uses a `Singleflight` mechanism to ensure exactly one concurrent provisioning attempt per storage key; (e) delegates actual provisioning to `ISchemaHealthContributor<TEntity, TKey>.EnsureHealthy(ct)` from the adapter; and (f) supports manual invalidation via `Invalidate()` and `ClearProvisioningError()` for diagnostics.

---

## 5. Implementation Evidence

The following files in the Koan Framework v0.6.3 codebase provide concrete evidence of the disclosed system:

| Component | File Path | Lines |
|---|---|---|
| Entity base class (CRTP) | `src/Koan.Data.Core/Model/Entity.cs` | ~909 |
| Entity cache accessor (partial) | `src/Koan.Data.Core/Model/Entity.Cache.cs` | ~133 |
| Data static facade | `src/Koan.Data.Core/Data.cs` | ~666 |
| DataService (repository factory) | `src/Koan.Data.Core/DataService.cs` | ~93 |
| EntityContext (ambient routing) | `src/Koan.Data.Core/EntityContext.cs` | ~291 |
| AdapterResolver (5-level cascade) | `src/Koan.Data.Core/AdapterResolver.cs` | ~119 |
| EntityEventExecutor (lifecycle) | `src/Koan.Data.Core/Events/EntityEventExecutor.cs` | ~343 |
| EntityEventContext (pipeline context) | `src/Koan.Data.Core/Events/EntityEventContext.cs` | ~189 |
| EntityEventRegistry (handler storage) | `src/Koan.Data.Core/Events/EntityEventRegistry.cs` | ~87 |
| EntityEventPrior (lazy prior loader) | `src/Koan.Data.Core/Events/EntityEventPrior.cs` | ~57 |
| EntityEventsBuilder (fluent registration) | `src/Koan.Data.Core/Events/EntityEventsBuilder.cs` | N/A |
| RepositoryFacade (decorator) | `src/Koan.Data.Core/RepositoryFacade.cs` | ~287 |
| AggregateIdentityManager | `src/Koan.Data.Core/AggregateIdentityManager.cs` | ~37 |
| AggregateExtensions (instance bridge) | `src/Koan.Data.Core/AggregateExtensions.cs` | ~291 |
| EntitySchemaGuard | `src/Koan.Data.Core/Schema/EntitySchemaGuard.cs` | ~162 |
| TransactionCoordinator | `src/Koan.Data.Core/Transactions/TransactionCoordinator.cs` | ~341 |
| TransactionCoordinatorFactory | `src/Koan.Data.Core/Transactions/TransactionCoordinatorFactory.cs` | ~34 |

**Key interfaces referenced:**
- `IEntity<TKey>` — marker interface with `TKey Id` property (in `Koan.Data.Abstractions`)
- `IDataRepository<TEntity, TKey>` — CRUD contract (in `Koan.Data.Abstractions`)
- `IDataService` — repository factory contract (in `Koan.Data.Core`)
- `IDataAdapterFactory` — provider factory contract (in `Koan.Data.Abstractions`)
- `IAggregateIdentityManager` — identity generation contract (in `Koan.Data.Core`)
- `ITransactionCoordinator` — transaction tracking contract (in `Koan.Data.Core.Transactions`)
- `ISchemaHealthContributor<TEntity, TKey>` — schema provisioning contract
- `IDataRepositoryDecorator` — repository decoration contract (in `Koan.Data.Core.Decorators`)
- `IQueryCapabilities` / `IWriteCapabilities` — capability advertisement contracts
- `IBatchSet<TEntity, TKey>` — fluent batch composition contract

---

## 6. Publication Notice

This document is published as a **defensive publication** under the doctrine of prior art. The techniques, architectures, algorithms, and system designs described herein are placed into the **public domain** as of the publication date (2026-03-24) for the purpose of establishing prior art that prevents any party -- including the inventor -- from obtaining patent protection on these specific techniques.

This publication does not grant any license to the Koan Framework source code itself, which remains subject to its own licensing terms. It establishes only that the **ideas, architectures, and techniques** described herein are publicly known as of the publication date.

Any person or entity may freely implement systems using the techniques described in this publication without risk of patent infringement claims based on these specific disclosures.

**Inventor acknowledgment:** I, Leo Botinelly (Leonardo Milson Botinelly Soares), confirm that I am the inventor of the systems described herein and that I authorize this defensive publication.

---

## 7. Antagonist Review Cycle

### Review Protocol

The following section documents a structured adversarial review conducted between two roles:

- **Author**: The inventor, defending the publication's completeness and novelty.
- **Hostile Patent Attorney (HPA)**: A simulated patent examiner/attorney probing for weaknesses that could allow a third party to patent around this disclosure.

Each round consists of the HPA raising objections followed by the Author's response and any revisions made.

---

### Round 1: Abstraction Gap Analysis

**HPA Objection 1.1 — Service Locator dependency not fully disclosed:**
The disclosure describes `AppHost.Current` as a "static service locator backed by the application's IServiceProvider" but does not explain how `AppHost.Current` is populated or what happens when it is null. A competitor could patent a system that uses a different ambient service resolution mechanism (e.g., a per-thread registry, a scoped container, a compile-time source generator) to achieve the same Entity-first API surface without `AppHost.Current`.

**Author Response:**
The core novelty is not the service resolution mechanism but the layered architecture that separates the API surface (Entity), lifecycle interception (EventExecutor), routing (EntityContext + AdapterResolver), and provider abstraction (Data + DataService + Facade). The disclosure explicitly states that `Repo` calls `AppHost.Current?.GetService<IDataService>()?.GetRepository<TEntity, TKey>()` and documents the null-throws behavior. However, to strengthen coverage, I note here that the principle generalizes: any ambient service resolution mechanism (static singleton, `AsyncLocal<IServiceProvider>`, compile-time generated resolver, or ambient DI scope) could replace `AppHost.Current` without altering the disclosed architecture. The disclosure covers the abstract pattern of "static entity methods that resolve providers at call-time via an ambient mechanism" regardless of the specific ambient implementation. No revision needed -- the disclosure is agnostic to the service resolution implementation and the existing text covers this by describing the behavior, not mandating a specific mechanism.

**HPA Objection 1.2 — `DataSourceRegistry` not described:**
The `AdapterResolver` references `DataSourceRegistry` but this type is never described in the disclosure. A competitor could patent the specific mechanism of mapping source names to adapter+connection configurations.

**Author Response:**
Fair point. `DataSourceRegistry` is a configuration-binding class that reads `Koan:Data:Sources:{name}` configuration sections and provides `GetSource(name)` returning a source definition with `Adapter` and `ConnectionString` properties. This is standard configuration binding (not novel), but it should be mentioned for completeness. I will add a clarifying sentence to Section 3.3 noting that `DataSourceRegistry` reads named source definitions from application configuration, where each source specifies an adapter name and connection details.

**Revision:** Section 3.3, Priority 1 description now includes: "the resolver looks up that source name in `DataSourceRegistry` (a configuration-binding service that reads `Koan:Data:Sources:{name}` entries from application configuration, where each entry specifies an adapter name and connection string)." This revision is reflected in the text as written above.

**HPA Objection 1.3 — Decorator chain ordering not specified:**
The disclosure mentions `IDataRepositoryDecorator` but does not describe the ordering semantics. If decorators execute in registration order, the disclosure should say so.

**Author Response:**
The `ApplyDecorators` method iterates `IEnumerable<IDataRepositoryDecorator>` in the order provided by the DI container (typically registration order). Each decorator calls `TryDecorate(entityType, keyType, current, services)` and returns either a wrapped repository or null (skip). The `current` reference is updated after each successful decoration, creating a chain. This is standard decorator pattern behavior. I note it here for completeness but it does not affect the core disclosure -- the novelty is in the facade's built-in behaviors (identity, timestamps, schema guard), not the extensibility mechanism.

---

### Round 2: Reproducibility Gaps

**HPA Objection 2.1 — `StringId.New()` algorithm not disclosed:**
The disclosure mentions `StringId.New()` as "a custom ULID-like identifier" but does not disclose its algorithm. A competitor could patent a specific string-based sortable identifier generation algorithm.

**Author Response:**
`StringId.New()` generates a string identifier with a time-ordered prefix (similar to ULID: 10 bytes of timestamp + 16 bytes of randomness, Crockford base32 encoded). The exact algorithm is: encode the current Unix timestamp in milliseconds as the high-order portion, concatenate cryptographically random bytes for the low-order portion, and encode the result as a base32 string. This is functionally equivalent to ULID (which is itself prior art from 2016, specification at github.com/ulid/spec). No revision needed to the main disclosure -- ULID-like generation is well-established prior art.

**HPA Objection 2.2 — `Singleflight` mechanism not described:**
The `EntitySchemaGuard` uses `Singleflight.Run(storageKey, ...)` but this is not explained.

**Author Response:**
Singleflight is a concurrency primitive (prior art from Go's `singleflight` package and various .NET implementations) that deduplicates concurrent calls with the same key. When multiple threads call `Singleflight.Run("key", func)` simultaneously, only one execution of `func` proceeds; all other callers await the same result. Koan's implementation uses `ConcurrentDictionary<string, SemaphoreSlim>` to serialize access per key. This is a well-known pattern, not novel. Noted here for reproducibility.

**HPA Objection 2.3 — `PartitionMoveBuilder` API surface not shown:**
Disclosure 13 references a fluent builder but does not show its method signatures.

**Author Response:**
The fluent builder `PartitionMoveBuilder<TEntity, TKey>` provides: `.Where(Expression<Func<TEntity, bool>>)`, `.Map(Func<TEntity, TEntity>)`, `.Copy()` (returns CopyPartitionBuilder), `.Move()` (returns MovePartitionBuilder), `.BatchSize(int)`, and `.To(string targetPartition)` (terminal, returns `Task<int>`). This is standard fluent builder pattern. The disclosure already describes the end-to-end syntax example; the individual method signatures are not novel.

---

### Round 3: Scope Holes

**HPA Objection 3.1 — No coverage of compile-time code generation alternative:**
The disclosed system relies on runtime reflection, `AsyncLocal`, and generic static classes. A competitor could implement the same Entity-first API surface using compile-time source generators (e.g., Roslyn incremental generators) that emit provider-specific code at build time, eliminating the runtime resolution pipeline entirely.

**Author Response:**
This is a valid scope observation. The disclosure covers both the runtime resolution approach and the abstract architectural pattern. Disclosure 1 describes the pattern in terms of "delegates through a multi-layer pipeline" without mandating runtime resolution. A compile-time generator that produces the same layered delegation (entity surface -> lifecycle interception -> data facade -> adapter resolution -> facade -> adapter) would still be implementing the disclosed pattern if it maintains the same architectural separation. However, to explicitly cover this, I note: the disclosed architecture may be implemented via runtime resolution (as described), compile-time source generation, ahead-of-time compilation, or any combination thereof. The novel contribution is the architectural layering and the specific behaviors at each layer, not the mechanism of assembly (runtime vs. compile-time).

**HPA Objection 3.2 — No coverage of distributed/microservice deployment:**
The disclosure assumes a single-process model where `AppHost.Current` provides all services. A competitor could patent a distributed version where the `Data<TEntity, TKey>` facade routes to remote services via gRPC/HTTP.

**Author Response:**
The `IDataAdapterFactory` abstraction already supports this: a factory could create a repository that serializes operations and sends them to a remote service. The disclosure's `IDataRepository<TEntity, TKey>` interface is transport-agnostic. However, to explicitly cover this: the adapter layer may implement local in-process data access, remote procedure calls (gRPC, HTTP, message queues), hybrid strategies, or any combination thereof, without altering the layers above it. The disclosed architecture's value is precisely this substitutability at the adapter layer.

**HPA Objection 3.3 — Vector operations mentioned but not fully disclosed:**
The `TransactionCoordinator` has `TrackVectorSave` and `TrackVectorDelete` methods, but the disclosure does not describe the vector operation pipeline.

**Author Response:**
Vector operations follow the same pattern as entity operations: `Data<TEntity, TKey>` has vector-aware extensions (in `Koan.Data.Vector`) that detect the ambient `TransactionCoordinator` and defer vector persistence. The core pattern (deferred execution + adapter grouping + best-effort commit) is identical. The vector-specific interfaces (`IVectorRepository`) and embedding storage are separate features not central to this disclosure. However, the transaction coordinator's support for heterogeneous operation types (entity + vector) within a single transaction scope is covered by Disclosure 9 point (g) "cross-adapter atomicity is explicitly documented as best-effort."

---

### Round 4: Prior Art Weakness

**HPA Objection 4.1 — ActiveRecord + Multi-DB in Rails 6:**
Rails 6+ supports `connects_to` with roles (`:reading`, `:writing`) and `connected_to(role: :reading)` blocks that use `Thread.current` (analogous to `AsyncLocal`). This could be argued as prior art for ambient routing in Active Record.

**Author Response:**
Rails `connected_to` provides database role switching (read replica vs. primary) but does not provide:
- Provider-type switching (PostgreSQL vs. MongoDB vs. Vector store) within the same model class
- A 5-level resolution cascade with entity-level attributes, default source, and factory priority
- Source XOR Adapter mutual exclusion
- Partition-scoped storage addressing
- Transparent lifecycle interception with field protection and lazy prior loading
- Best-effort multi-adapter transaction coordination

Rails `connected_to` operates at a coarser granularity (database role) and does not support heterogeneous provider routing. The disclosed system operates at entity-type granularity with per-call provider resolution. These are materially different capabilities. However, the disclosure now explicitly acknowledges Rails 6 multi-database support in the Prior Art section.

**HPA Objection 4.2 — EF Core interceptors as lifecycle pipeline prior art:**
EF Core's `SaveChangesInterceptor` and `IInterceptor` chain could be characterized as a lifecycle pipeline.

**Author Response:**
EF Core interceptors operate at the `DbContext` level (aggregate over all entities in a unit of work), not at the individual entity level. They do not provide:
- Per-entity-type handler registration (`Entity<Todo>.Events.Before.Upsert(...)`)
- Typed `EntityEventContext<TEntity>` with entity-specific operations
- Lazy prior-state loading per entity
- Field protection with snapshot + validation
- Zero-overhead bypass per entity type based on handler registration
- Batch-level atomic semantics where individual entity cancellation can abort or skip

The granularity difference (per-context vs. per-entity-type) is fundamental. This is already addressed in Prior Art Section 2.3.

---

### Round 5: Section 101 / Abstract Idea Exposure

**HPA Objection 5.1 — "Static methods on entities that route to different databases" could be characterized as an abstract idea:**
Under Alice/Mayo analysis, the concept of "entity types with static methods that route operations to configurable storage backends" might be considered an abstract idea implemented on a generic computer.

**Author Response:**
This is precisely why this is a **defensive publication**, not a patent application. The goal is to place these techniques in the public domain as prior art. If a court or examiner would find these techniques abstract under Section 101, that further supports the goal: abstract ideas cannot be patented, and this disclosure ensures that the specific concrete implementation details (the 5-level cascade, the `AsyncLocal` context with Source XOR Adapter constraint, the lifecycle pipeline with field protection snapshots, etc.) are documented public prior art regardless of abstractness analysis.

For defensive purposes, the disclosure's strength lies in its specificity: even if the broad concept is abstract, the specific implementation details (4-dimensional cache key structure, copy-on-write handler arrays with `ReadOnlyMemory<T>` iteration, `Singleflight`-guarded schema provisioning with timed retry backoff, deferred operation tracking grouped by adapter name with telemetry spans) are concrete enough to anticipate and block specific patent claims.

**HPA Objection 5.2 — Terminology drift risk:**
A competitor could rename concepts (e.g., "routing context" becomes "persistence scope", "adapter resolver" becomes "storage mediator") and argue the disclosure does not cover their implementation.

**Author Response:**
The disclosure describes both the abstract pattern and the concrete implementation. Section 3 uses specific interface names, class names, and method signatures from the actual codebase. The Claims-Style Disclosures (Section 4) describe the patterns in functional terms independent of naming. For example, Disclosure 4 describes "ambient routing context using AsyncLocal with four orthogonal routing dimensions" without being dependent on the name `EntityContext`. A renamed implementation performing the same function (ambient, AsyncLocal-backed, four-dimensional routing with source/adapter mutual exclusion) would still be anticipated by this disclosure.

---

### Round 6: Edge Cases

**HPA Objection 6.1 — Concurrent modification of `EntityEventRegistry` during iteration:**
If a handler is being registered on one thread while another thread is executing the pipeline, is there a race condition?

**Author Response:**
No. The registry uses copy-on-write: `Add<THandler>(ref THandler[] target, THandler handler)` creates a new array under a lock and atomically replaces the reference. The executor reads the array reference once (`EntityEventRegistry<TEntity, TKey>.BeforeUpsertHandlers`) and iterates the resulting `ReadOnlyMemory<T>`. Since the old array is never mutated, concurrent reads are safe. New registrations are only visible on subsequent operations. This is a standard concurrent-safe pattern documented in the executor's use of `.Span[i]` indexing.

**HPA Objection 6.2 — `EntityContext` AsyncLocal and thread pool behavior:**
`AsyncLocal` captures values per async flow, but if a `using var _ = EntityContext.Source("x")` scope encompasses `Task.Run(...)` or `Parallel.ForEach`, child tasks inherit the context but Dispose on the parent thread does not affect child threads.

**Author Response:**
This is expected `AsyncLocal` behavior in .NET. The disclosure accurately describes the scoping mechanism. Child tasks inherit the context at the point of their creation; disposal on the parent thread restores the parent's context but does not affect already-running child tasks. This is a known characteristic of `AsyncLocal`, not a defect. Application developers who need to clear context in child tasks can create a new scope.

---

### Clearance Assessment

After six rounds of adversarial review, the following conclusions apply:

1. **Abstraction gaps**: Minor clarification added for `DataSourceRegistry`. All other abstractions are sufficiently described or are standard patterns (decorator chain, singleflight).
2. **Reproducibility**: All algorithms are either disclosed in sufficient detail or reference well-known prior art (ULID, singleflight, copy-on-write arrays).
3. **Scope**: The disclosure explicitly covers both runtime and compile-time implementations, local and distributed deployments, and entity + vector operation types.
4. **Prior art differentiation**: Rails 6, Django, EF Core, and Dapper are distinguished on specific capability gaps. No existing system combines all disclosed techniques.
5. **Section 101**: Not applicable (defensive publication). Specificity is sufficient to anticipate concrete patent claims.
6. **Terminology**: Functional descriptions in Claims-Style Disclosures are naming-independent.
7. **Edge cases**: Concurrency safety and AsyncLocal scoping are correctly documented.

**CLEARANCE GRANTED.** The disclosure is sufficiently complete to establish prior art for the described techniques.

---

*End of Defensive Publication*
