# Defensive Patent Publication

## Entity-Centric Schema Guard with Singleflight Provisioning, Error Caching, and Manual Reset

**Publication Type:** Defensive Patent Publication (prior art disclosure)
**Inventor:** Leo Botinelly (Leonardo Milson Botinelly Soares)
**Date of Disclosure:** 2026-03-24
**Framework:** Koan Framework v0.6.3 (.NET, target net10.0)
**Repository:** github.com/koan-framework (Koan.Data.Core assembly)

---

## 1. Abstract

This disclosure describes a system and method for one-time, lazy schema provisioning per unique combination of (entity type, data adapter, data source, logical partition) at the API boundary of a multi-provider entity persistence framework. The system introduces three cooperating mechanisms: (a) an `EntitySchemaGuard<TEntity, TKey>` that interposes on every data-access call through a `RepositoryFacade` and tracks per-storage-key provisioning state in a static `ConcurrentDictionary<string, ProvisionState>`, (b) a `Singleflight` deduplication primitive that ensures only one provisioning operation executes per storage key at any given time while all concurrent callers await the same in-flight task, and (c) a time-bounded error cache that records provisioning failures with attempt counts and prevents retry stampede by rejecting re-provisioning attempts within a five-minute window after failure. The system additionally provides manual reset capability via `Invalidate()` and `ClearProvisioningError()` methods that remove cached state, clear Singleflight entries, and delegate invalidation to the underlying adapter. Schema health checking is abstracted behind `ISchemaHealthContributor<TEntity, TKey>`, which each data adapter (Postgres, SQL Server, SQLite, MongoDB, Couchbase) implements to perform adapter-specific operations such as table creation, index creation, collection provisioning, or bucket scope verification. An `AggregateSchemaHealthContributor<TEntity, TKey>` bridges the generic interface to the concrete repository resolved at runtime, enabling the guard to function without compile-time knowledge of which adapter is active. The guard is registered as a singleton open generic (`typeof(EntitySchemaGuard<,>)`) via dependency injection, and the `RepositoryFacade` calls `EnsureHealthy()` before every repository operation (Get, Query, Upsert, Delete, Batch, Execute), making schema provisioning transparent to application code.

---

## 2. Technical Problem

Data persistence frameworks that support multiple storage backends (relational databases, document stores, key-value stores, vector databases) face a recurring problem: the backing store's schema must exist before data operations can succeed. Tables must be created, indexes must be established, collections must be provisioned, and bucket scopes must be verified. Existing approaches to this problem suffer from several deficiencies:

**Problem 1 -- Startup-only provisioning creates deployment fragility.** Frameworks such as Entity Framework Core run migrations at application startup via `Database.EnsureCreated()` or `Database.Migrate()`. This approach fails when: (a) the database is temporarily unavailable during startup, causing the entire application to fail to boot; (b) new entity types are registered dynamically after startup (e.g., plugin architectures, multi-tenant systems with per-tenant entity types); (c) the application connects to multiple databases where some may be unavailable at boot time. A failed startup migration requires a full application restart to retry.

**Problem 2 -- Per-operation schema checks impose unacceptable overhead.** The naive alternative -- checking schema health before every database operation -- introduces a network round-trip per operation. In a system processing thousands of entity operations per second across dozens of entity types, this overhead is prohibitive. No existing framework provides a mechanism to perform schema checks exactly once per entity-storage combination and then skip all subsequent checks.

**Problem 3 -- Concurrent provisioning causes duplicate work or conflicts.** When multiple HTTP requests arrive simultaneously for a new entity type whose backing table does not yet exist, naive per-operation provisioning causes multiple concurrent `CREATE TABLE IF NOT EXISTS` or `CREATE INDEX` operations. While idempotent DDL operations may tolerate this, they waste resources, hold database locks, and can cause deadlocks on some storage engines (e.g., concurrent index creation on PostgreSQL, concurrent collection creation on MongoDB).

**Problem 4 -- Failure retry stampede degrades availability.** When schema provisioning fails (e.g., database permission denied, storage quota exceeded, network partition), every subsequent data operation immediately retries provisioning. Under high request volume, this creates a retry stampede where hundreds of concurrent requests simultaneously attempt the same failing operation, amplifying load on the already-stressed storage system and consuming application thread pool resources.

**Problem 5 -- No manual recovery path for cached errors.** Frameworks that cache schema state (e.g., Hibernate's schema validation cache) provide no mechanism for operators to clear a cached error after fixing the underlying problem. The only recovery path is application restart, which is disruptive in production environments with long-running connections, in-memory caches, and active user sessions.

**Problem 6 -- Schema provisioning is decoupled from data access topology.** In multi-provider frameworks where the same entity type can be stored in different adapters (Postgres in production, SQLite in development, MongoDB for a specific tenant), schema provisioning must be aware of the active adapter, data source, and logical partition. No existing framework provides per-(entity, adapter, source, partition) provisioning granularity.

---

## 3. Solution

The disclosed system introduces a three-layer architecture for entity-centric schema provisioning that operates transparently at the API boundary of every data operation.

### 3.1 EntitySchemaGuard -- Per-Entity Provisioning Coordinator

`EntitySchemaGuard<TEntity, TKey>` is an `internal sealed` generic class registered as a singleton open generic in the dependency injection container. It maintains a static `ConcurrentDictionary<string, ProvisionState>` that tracks provisioning outcomes keyed by a composite storage key.

**Storage key computation.** The key is computed at runtime from the current entity context:

```
"{adapter}:{source}:{partition}"
```

Where `adapter` is the resolved data adapter name (e.g., "postgres", "mongo", "sqlite"), `source` is the resolved data source name, and `partition` is the current logical partition from `EntityContext.Current?.Partition ?? "root"`. This key is computed via `AdapterResolver.ResolveForEntity<TEntity>()` and the `DataSourceRegistry`, ensuring the guard is aware of the active multi-provider routing.

**Provisioning state.** Each storage key maps to a `ProvisionState` record containing:

- `IsProvisioned` (bool): Whether provisioning has completed successfully.
- `ProvisionedAt` (DateTime?): UTC timestamp of successful provisioning.
- `Error` (ProvisionError?): Cached failure information, if any.

**ProvisionError** captures `Message` (string), `FailedAt` (DateTime), and `AttemptCount` (int).

**EnsureHealthy algorithm.** On every data operation, the `RepositoryFacade` calls `EnsureHealthy(ct)`:

1. **Resolve contributor.** Lazily resolve `ISchemaHealthContributor<TEntity, TKey>` from the DI container via `_services.GetService<>()`. Cache the result (including null) with a `_attempted` boolean to avoid repeated service resolution. If no contributor is registered, return immediately -- the entity type does not require schema provisioning.

2. **Compute storage key.** Build the composite key from the current adapter, source, and partition context.

3. **Check cached state.** If `_states[key].IsProvisioned` is true, return immediately. This is the fast path that executes for the vast majority of operations after initial provisioning.

4. **Check error cache.** If `_states[key].Error` is non-null and the error is less than five minutes old, throw `InvalidOperationException` with the original error message, remaining cooldown time, and attempt count. This prevents retry stampede.

5. **Enter Singleflight.** Call `Singleflight.Run(key, async token => { ... }, ct)`. Inside the Singleflight delegate:
   - **Double-check.** Re-read `_states[key]` inside the Singleflight to handle the race between the outer check and Singleflight entry. If now provisioned, return.
   - **Execute provisioning.** Call `contributor.EnsureHealthy(token)`.
   - **On success.** Store `new ProvisionState(true, DateTime.UtcNow, null)` into `_states[key]`. Log via structured `KoanLog.DataDebug`.
   - **On failure.** Increment `AttemptCount` from the previous error (if any), store `new ProvisionState(false, null, new ProvisionError(ex.Message, DateTime.UtcNow, attemptCount + 1))`. Log the failure. Re-throw the exception so the current caller receives the error.

### 3.2 Singleflight -- Concurrent Deduplication Primitive

`Singleflight` is a public static class in `Koan.Core.Infrastructure` that deduplicates concurrent asynchronous operations by string key. It maintains a static `ConcurrentDictionary<string, Lazy<Task>>` of in-flight operations.

**Run semantics.** `Singleflight.Run(key, work, ct)`:

1. Create a `Lazy<Task>` wrapping the work delegate with `LazyThreadSafetyMode.ExecutionAndPublication`, ensuring the delegate executes at most once.
2. Call `_inflight.GetOrAdd(key, _ => MakeLazy())`. If the key already exists, the existing `Lazy<Task>` is returned -- the second caller will await the same task as the first caller.
3. Await `lazy.Value`. This triggers the delegate on first access; subsequent accesses return the same `Task`.
4. In the `finally` block, remove the key from `_inflight`, allowing future calls to start a new operation.

**Typed variant.** `RunAsync<T>(key, work, ct)` wraps the work in a `TaskCompletionSource<T>` to propagate typed results through the Singleflight barrier, handling success, cancellation, and exception cases.

**Manual invalidation.** `Singleflight.Invalidate(key)` removes the key from `_inflight`, which does not cancel any in-flight task but allows the next caller to start a new operation.

### 3.3 Error Caching with Time-Bounded Backoff

The error cache operates as follows:

- **Cache on failure.** When `contributor.EnsureHealthy()` throws, the exception message, current UTC time, and incremented attempt count are stored as a `ProvisionError` within the `ProvisionState`.

- **Reject within window.** On subsequent calls to `EnsureHealthy()`, if a cached error exists and `DateTime.UtcNow - error.FailedAt < 5 minutes`, the guard throws immediately without contacting the storage system. The exception message includes the remaining cooldown time and attempt number for operational diagnostics.

- **Allow retry after window.** After five minutes elapse, the guard allows re-entry into the Singleflight provisioning path, which will produce a fresh attempt.

- **Attempt counting.** The `AttemptCount` field increments across retry windows, providing operational visibility into how many times provisioning has been attempted for a given storage key.

### 3.4 Manual Reset Capability

Two reset mechanisms are provided:

**`Invalidate()`.** Performs a full reset:
1. Removes the storage key from `_states` (clearing both success and error state).
2. Calls `contributor.InvalidateHealth()` to clear adapter-local caches (e.g., schema introspection caches in the Postgres or MongoDB adapter).
3. Calls `Singleflight.Invalidate(key)` to allow new provisioning operations.
4. Logs the invalidation event.

The next data operation will trigger a fresh provisioning attempt.

**`ClearProvisioningError()`.** Performs a targeted error reset:
1. Removes the storage key from `_states`.
2. Logs the error-cleared event.

This allows operators to clear a cached provisioning failure (e.g., after granting database permissions) without invalidating the adapter's internal caches.

### 3.5 ISchemaHealthContributor -- Adapter Abstraction

The `ISchemaHealthContributor<TEntity, TKey>` interface defines two methods:

- `Task EnsureHealthy(CancellationToken ct)` -- Perform adapter-specific schema provisioning (create tables, indexes, collections, bucket scopes, etc.).
- `void InvalidateHealth()` -- Clear adapter-local caches so the next `EnsureHealthy` call recomputes schema state from scratch.

This interface is implemented by five data adapters:

| Adapter | EnsureHealthy Behavior |
|---------|----------------------|
| PostgresRepository | Creates table via DDL, creates indexes for annotated properties |
| SqlServerRepository | Creates table via DDL, creates indexes |
| SqliteRepository | Creates table via DDL, creates indexes |
| MongoRepository | Creates collection, ensures indexes on annotated properties |
| CouchbaseRepository | Verifies bucket scope, creates collection |

### 3.6 AggregateSchemaHealthContributor -- Runtime Bridge

`AggregateSchemaHealthContributor<TEntity, TKey>` is an internal sealed class that resolves the concrete repository for an entity type at runtime via `AggregateConfigs.Get<TEntity, TKey>(services).Repository` and delegates `EnsureHealthy()` and `InvalidateHealth()` calls to it if the repository implements `ISchemaHealthContributor<TEntity, TKey>`. This indirection allows the `EntitySchemaGuard` to function without compile-time knowledge of which adapter is active -- the adapter is selected by the framework's multi-provider routing infrastructure.

### 3.7 RepositoryFacade Integration

`RepositoryFacade<TEntity, TKey>` is an internal sealed class that wraps every data repository with cross-cutting concerns. It holds a reference to `EntitySchemaGuard<TEntity, TKey>` and calls `_schemaGuard.EnsureHealthy(ct)` via a `Guard(ct)` method before every operation:

- `Get`, `GetMany`, `Query` (all overloads), `Count` -- read operations
- `Upsert`, `UpsertMany` -- write operations
- `Delete`, `DeleteMany`, `DeleteAll`, `RemoveAll` -- delete operations
- `ExecuteAsync` -- instruction execution
- `BatchFacade.Save` -- batch operations

This ensures schema provisioning is transparent to application code. The entity-first API (`Todo.Get(id)`, `todo.Save()`) flows through the `RepositoryFacade`, which triggers provisioning on first access.

---

## 4. Implementation Architecture

### 4.1 Type System

| Type | Visibility | Assembly | Role |
|------|-----------|----------|------|
| `EntitySchemaGuard<TEntity, TKey>` | internal sealed | Koan.Data.Core | Per-entity provisioning coordinator with state tracking, error caching, and Singleflight integration |
| `ProvisionState` | private record (nested) | Koan.Data.Core | Immutable state record: `IsProvisioned`, `ProvisionedAt`, `Error` |
| `ProvisionError` | private record (nested) | Koan.Data.Core | Immutable error record: `Message`, `FailedAt`, `AttemptCount` |
| `ISchemaHealthContributor<TEntity, TKey>` | public interface | Koan.Data.Core | Adapter contract for schema provisioning and cache invalidation |
| `AggregateSchemaHealthContributor<TEntity, TKey>` | internal sealed | Koan.Data.Core | Runtime bridge from generic interface to concrete repository |
| `Singleflight` | public static | Koan.Core | Concurrent deduplication primitive with `ConcurrentDictionary<string, Lazy<Task>>` |
| `RepositoryFacade<TEntity, TKey>` | internal sealed | Koan.Data.Core | Cross-cutting wrapper that calls `EnsureHealthy()` before every operation |
| `DataSourceRegistry` | internal | Koan.Data.Core | Registry of configured data sources used for storage key computation |
| `AdapterResolver` | internal static | Koan.Data.Core | Resolves active adapter and source for a given entity type |
| `EntityContext` | public static | Koan.Data.Core | Ambient context providing current partition for storage key computation |

### 4.2 Data Flow Diagram

```
Application Code: Todo.Get(id)
  |
  v
Entity<T> static method
  |
  v
RepositoryFacade<Todo, Guid>.Get(id, ct)
  |
  v
Guard(ct)
  |
  +-- ct.ThrowIfCancellationRequested()
  |
  +-- _schemaGuard.EnsureHealthy(ct)
       |
       +-- GetContributor()
       |     |
       |     +-- [_attempted == true] --> return cached _contributor (may be null)
       |     +-- [_attempted == false] --> _services.GetService<ISchemaHealthContributor<Todo, Guid>>()
       |                                   --> AggregateSchemaHealthContributor<Todo, Guid>
       |                                        --> AggregateConfigs.Get<Todo, Guid>(services).Repository
       |                                        --> PostgresRepository<Todo, Guid> (implements ISchemaHealthContributor)
       |
       +-- [contributor is null] --> return (no provisioning needed)
       |
       +-- BuildStorageKey()
       |     --> AdapterResolver.ResolveForEntity<Todo>(services, sourceRegistry)
       |     --> "{adapter}:{source}:{partition}" e.g. "postgres:default:root"
       |
       +-- [_states[key].IsProvisioned == true] --> return (fast path)
       |
       +-- [_states[key].Error != null && age < 5 min] --> throw (error cache hit)
       |
       +-- Singleflight.Run(key, async token => {
       |     |
       |     +-- [double-check: _states[key].IsProvisioned] --> return
       |     |
       |     +-- contributor.EnsureHealthy(token)
       |     |     --> PostgresRepository: CREATE TABLE IF NOT EXISTS, CREATE INDEX, etc.
       |     |
       |     +-- [success] --> _states[key] = ProvisionState(true, UtcNow, null)
       |     +-- [failure] --> _states[key] = ProvisionState(false, null, ProvisionError(...))
       |                       --> re-throw
       |   }, ct)
       |
  v
_inner.Get(id, ct)  -- actual database query
  |
  v
return TEntity?
```

### 4.3 State Machine

```
                    ┌──────────────┐
                    │  Untracked   │  (no entry in _states)
                    └──────┬───────┘
                           │ First EnsureHealthy() call
                           v
                    ┌──────────────┐
              ┌─────│ Provisioning │  (inside Singleflight)
              │     └──────┬───────┘
              │            │
         exception      success
              │            │
              v            v
    ┌─────────────┐  ┌──────────────┐
    │  Error       │  │ Provisioned  │
    │  (cached)   │  │ (terminal)   │
    └──────┬──────┘  └──────┬───────┘
           │                │
    age >= 5 min       Invalidate()
           │                │
           v                v
    ┌──────────────┐  ┌──────────────┐
    │ Provisioning │  │  Untracked   │
    │ (retry)      │  │  (re-enter)  │
    └──────────────┘  └──────────────┘
           │
    ClearProvisioningError()
           │
           v
    ┌──────────────┐
    │  Untracked   │
    └──────────────┘
```

### 4.4 Registration and Lifecycle

```
ServiceCollectionExtensions (Koan.Data.Core):
  services.TryAddSingleton(typeof(EntitySchemaGuard<,>));

DI Resolution:
  EntitySchemaGuard<Todo, Guid> is resolved as singleton.
  _states is static (shared across all instances of same closed generic).
  _contributor is per-instance (resolved lazily from IServiceProvider).

AggregateConfig construction:
  var guard = sp.GetRequiredService<EntitySchemaGuard<TEntity, TKey>>();
  --> passed to RepositoryFacade constructor.
```

---

## 5. Novelty and Non-Obvious Aspects

The following aspects of this system, individually and in combination, constitute novel contributions to the state of the art in schema provisioning for multi-provider entity frameworks.

### 5.1 Per-(Entity, Adapter, Source, Partition) Provisioning Granularity (Novel)

No existing ORM, entity framework, or data access library provides schema provisioning scoped to the combination of entity type, active data adapter, data source, and logical partition. EF Core migrations run per-DbContext (which maps to one database); Flyway and Liquibase run per-database; Django migrations run per-app. The disclosed system computes a composite storage key at runtime from the current multi-provider routing state, enabling scenarios where the same entity type is provisioned independently in Postgres for one tenant and MongoDB for another, or in the "orders" partition vs. the "archive" partition, without interference.

### 5.2 API-Boundary Lazy Provisioning with One-Time Guarantee (Novel)

The disclosed system provisions schema lazily on first data access rather than eagerly at startup, yet guarantees that provisioning executes exactly once per storage key for the lifetime of the process. This is achieved through the combination of the `ConcurrentDictionary` state check (fast path), the Singleflight deduplication (concurrent safety), and the `IsProvisioned` flag (one-time guarantee). No existing framework provides this combination. EF Core's `EnsureCreated()` is startup-only; Dapper has no schema management; ActiveRecord migrations are CLI-driven.

### 5.3 Singleflight Deduplication for Schema Operations (Novel in Context)

While the Singleflight pattern (originating from Go's `singleflight` package) is known in the caching domain, its application to schema provisioning in an entity framework is novel. The specific implementation using `ConcurrentDictionary<string, Lazy<Task>>` with `LazyThreadSafetyMode.ExecutionAndPublication` provides both deduplication (multiple callers await the same task) and cleanup (the `finally` block removes the entry, allowing future operations after invalidation). The typed variant `RunAsync<T>` uses `TaskCompletionSource<T>` to propagate results, cancellation, and exceptions through the deduplication barrier.

### 5.4 Time-Bounded Error Caching with Attempt Counting (Novel)

The five-minute error cache window prevents retry stampede without permanently blocking provisioning. The `ProvisionError` record tracks `AttemptCount` across retry windows, providing operational observability into how many cycles the system has attempted. The combination of time-bounded rejection (throw with remaining cooldown), automatic retry enablement after the window expires, and manual override via `ClearProvisioningError()` creates a three-tier error handling strategy not found in any existing framework.

### 5.5 Dual Reset Mechanisms with Different Scopes (Non-Obvious)

The system provides two distinct reset operations with deliberately different scopes:

- `Invalidate()` performs a full reset: clears provisioning state, invalidates adapter-local caches (e.g., schema introspection results), and clears the Singleflight entry. This is appropriate after schema changes (e.g., adding a column).
- `ClearProvisioningError()` clears only the cached error without invalidating adapter caches. This is appropriate after fixing a permission issue or network problem where the schema itself has not changed.

This separation reflects the operational reality that "the provisioning failed" and "the schema changed" are different failure modes requiring different recovery actions.

### 5.6 Transparent Integration via RepositoryFacade (Non-Obvious)

The guard is invoked by the `RepositoryFacade` before every operation (reads, writes, deletes, batches, instruction execution), making schema provisioning completely transparent to application code. The entity-first API (`Todo.Get(id)`, `todo.Save()`) triggers provisioning without any explicit call from the developer. This is architecturally distinct from EF Core's approach where `EnsureCreated()` must be called explicitly, or Flyway/Liquibase where migrations must be run as a separate step.

### 5.7 Contributor Resolution via AggregateSchemaHealthContributor Bridge (Non-Obvious)

The `AggregateSchemaHealthContributor<TEntity, TKey>` resolves the concrete repository at runtime via `AggregateConfigs.Get<TEntity, TKey>(services).Repository` and checks whether it implements `ISchemaHealthContributor<TEntity, TKey>`. This allows the guard to work with any adapter without compile-time coupling, and gracefully degrades when an adapter does not support schema provisioning (e.g., a read-only API adapter). The lazy resolution with `_attempted` flag ensures service resolution occurs at most once per guard instance.

### 5.8 Combined System (Novel)

The integration of API-boundary lazy provisioning, Singleflight deduplication, time-bounded error caching with attempt counting, dual-scope reset mechanisms, per-(entity, adapter, source, partition) storage keys, and transparent RepositoryFacade integration into a coherent system represents a novel architecture for schema management in multi-provider entity frameworks. No existing ORM, migration tool, or data access library provides this combination of capabilities.

---

## 6. Prior Art Comparison

| Capability | EF Core Migrations | Flyway / Liquibase | Django Migrations | Rails ActiveRecord | Dapper | Koan Framework (this disclosure) |
|---|---|---|---|---|---|---|
| Provisioning trigger | Startup (`EnsureCreated`, `Migrate`) | CLI / startup hook | CLI (`manage.py migrate`) | CLI (`rails db:migrate`) | None | Lazy, on first data access per storage key |
| Provisioning scope | Per-DbContext (one database) | Per-database | Per-app | Per-database | N/A | Per-(entity, adapter, source, partition) |
| Concurrent deduplication | None (startup-only) | Database advisory locks | Database migration table lock | Database advisory locks | N/A | Singleflight with `Lazy<Task>` -- no database lock required |
| Error caching | None (fails startup) | None (fails CLI) | None (fails CLI) | None (fails CLI) | N/A | 5-minute time-bounded cache with attempt counting |
| Retry stampede prevention | N/A (no retry) | N/A (CLI retry) | N/A (CLI retry) | N/A (CLI retry) | N/A | Error cache rejects attempts within window |
| Manual error reset | Restart application | Re-run CLI | Re-run CLI | Re-run CLI | N/A | `ClearProvisioningError()` without restart |
| Full schema invalidation | Restart application | Re-run CLI | Re-run CLI | Re-run CLI | N/A | `Invalidate()` clears state + adapter caches + Singleflight |
| Multi-provider awareness | Single provider per DbContext | Single database type | Single database type | Single database type | N/A | Routes provisioning through adapter resolver; same entity, different backends |
| Transparent to application code | No (`EnsureCreated()` explicit) | No (separate tool) | No (separate command) | No (separate command) | N/A | Yes (RepositoryFacade calls guard before every operation) |
| Zero overhead when no schema needed | N/A | N/A | N/A | N/A | Yes (no schema layer) | Yes (null contributor short-circuits; `_attempted` flag prevents repeated DI resolution) |
| Adapter-specific provisioning | EF-specific DDL generation | SQL-based migrations | Django ORM DDL | ActiveRecord DDL | N/A | `ISchemaHealthContributor` per adapter (Postgres, SQL Server, SQLite, MongoDB, Couchbase) |

### Key Differentiators from Closest Prior Art (EF Core)

1. **EF Core provisions at startup, not at API boundary.** `Database.EnsureCreated()` or `Database.Migrate()` must be called explicitly during application initialization. If the database is unavailable at startup, the application fails to boot. The disclosed system defers provisioning to first data access, tolerating database unavailability at startup.

2. **EF Core provisions per-DbContext, not per-entity.** A single `EnsureCreated()` call provisions all tables for all entity types in the DbContext. The disclosed system provisions per-entity, per-adapter, per-partition, enabling fine-grained control and supporting scenarios where different entities route to different storage backends.

3. **EF Core has no Singleflight deduplication.** If multiple services call `EnsureCreated()` concurrently (e.g., in a microservices startup race), each issues independent DDL. The disclosed system deduplicates concurrent provisioning for the same storage key.

4. **EF Core has no error caching or retry backoff.** A failed migration throws immediately on every subsequent call. The disclosed system caches failures for five minutes with attempt tracking.

5. **EF Core has no manual reset without restart.** Clearing EF Core's internal schema cache requires disposing the DbContext and rebuilding the service provider. The disclosed system provides `Invalidate()` and `ClearProvisioningError()` for in-process recovery.

---

## 7. Antagonist Analysis

This section anticipates challenges to the novelty and non-obviousness of the disclosed system and provides counterarguments.

### Challenge 1: "Lazy initialization is a well-known pattern; applying it to schema provisioning is obvious."

**Response.** Lazy initialization as a general technique is well established. The novelty lies not in lazy initialization itself but in the specific architecture that enables it for schema provisioning across a multi-provider entity framework. The system must solve three problems simultaneously: (a) computing a composite storage key at runtime from the current adapter routing context, which requires integration with the multi-provider resolution infrastructure; (b) deduplicating concurrent lazy provisioning via Singleflight, which is necessary because unlike typical lazy initialization of in-memory objects, schema provisioning involves external I/O that can take seconds and must not be duplicated; and (c) caching provisioning errors with time-bounded backoff, which is necessary because unlike in-memory initialization that either succeeds or throws, schema provisioning can fail transiently and must be retryable without stampede. The combination of these three concerns produces a system qualitatively different from `Lazy<T>` or `LazyInitializer.EnsureInitialized()`. No existing framework has applied this combination to schema provisioning despite lazy initialization being available for over two decades.

### Challenge 2: "The Singleflight pattern is directly borrowed from Go's sync/singleflight package."

**Response.** The Singleflight concept of deduplicating concurrent calls for the same key is indeed borrowed from Go. The novelty claim is not on the Singleflight primitive itself but on: (a) its application to schema provisioning in an entity framework, which is a novel use case -- Go's singleflight is typically used for cache population; (b) the specific .NET implementation using `ConcurrentDictionary<string, Lazy<Task>>` with `LazyThreadSafetyMode.ExecutionAndPublication`, which provides different semantics than Go's channel-based implementation (notably, the `Lazy<Task>` approach integrates with .NET's `async/await` model and the `finally`-based cleanup ensures entries are removed after completion); and (c) the integration with the error caching layer, where Singleflight prevents concurrent provisioning but does not prevent retry after the error cache window expires, creating a two-layer deduplication strategy (Singleflight for concurrent requests, error cache for sequential retries).

### Challenge 3: "Error caching with TTL is a standard caching pattern. Five minutes is an arbitrary choice."

**Response.** TTL-based caching is indeed standard. The non-obvious aspects are: (a) the decision to cache provisioning *errors* rather than provisioning *success* -- success is cached permanently (until explicit invalidation), while errors are cached temporarily, creating an asymmetric caching strategy that reflects the different semantics of success (schema exists, unlikely to un-exist) and failure (transient, should be retryable); (b) the attempt counting across TTL windows, which provides operational observability without affecting behavior -- the count persists from the previous error record and increments monotonically; (c) the interaction with Singleflight: within the five-minute window, the error cache rejects requests *before* entering Singleflight, avoiding even the overhead of deduplication. After the window expires, Singleflight re-engages to prevent concurrent retries. This layered rejection is not a standard caching pattern. The five-minute duration is a domain-appropriate default for infrastructure provisioning operations where the underlying issue (permissions, quotas, connectivity) typically takes minutes to resolve, and is intended to be tunable.

### Challenge 4: "RepositoryFacade is just a decorator pattern; intercepting operations is not novel."

**Response.** The decorator/proxy pattern for adding cross-cutting concerns to repository operations is indeed well known. The novelty lies in what the decorator does, not in the decorator pattern itself. Specifically, the `RepositoryFacade` integrates schema provisioning into the data access pipeline in a way that: (a) makes provisioning completely transparent to both application developers and entity-first API users (`Todo.Get(id)` triggers provisioning without any explicit schema management call); (b) operates before every category of operation (reads, writes, deletes, batches, raw instruction execution) through a single `Guard(ct)` method; and (c) delegates to an `EntitySchemaGuard` that itself orchestrates Singleflight deduplication, error caching, and adapter-agnostic health checking. No existing ORM's repository decorator or interceptor combines these responsibilities.

### Challenge 5: "Database-level locks (as used by Flyway/Liquibase) are a better solution for concurrent provisioning than application-level Singleflight."

**Response.** Database advisory locks and migration table locks solve a different problem: coordinating schema changes across multiple application instances (horizontal scaling). The disclosed Singleflight solves concurrent provisioning within a single application instance, which is the relevant problem for per-entity lazy provisioning triggered by concurrent HTTP requests. The two approaches are complementary, not competing: Singleflight prevents redundant provisioning calls from the same process, while database locks (which individual adapters may use within their `EnsureHealthy()` implementation) prevent conflicts across processes. Furthermore, Singleflight operates without requiring database connectivity, which is essential for the error caching scenario where the database may be temporarily unreachable -- a database lock cannot be acquired when the database is down, but Singleflight can still deduplicate and cache the failure.

### Challenge 6: "The storage key computation is just string concatenation; per-entity provisioning granularity is an obvious extension."

**Response.** The storage key format (`{adapter}:{source}:{partition}`) appears simple, but achieving it requires deep integration with the framework's multi-provider routing infrastructure. The key is computed by calling `AdapterResolver.ResolveForEntity<TEntity>()` with the current `DataSourceRegistry`, which consults entity-type-to-adapter mappings, default source configurations, and ambient `EntityContext` partition state. This resolution is the same mechanism used for actual data routing, ensuring that the provisioning guard tracks the same storage destination that will receive the actual data operation. Computing this key is not possible in frameworks that lack multi-provider routing (EF Core, Dapper, ActiveRecord) or that route at the database level rather than the entity level (Flyway, Liquibase). The per-entity granularity is novel because it is enabled by a per-entity routing architecture that does not exist in prior art.

---

## Appendix A: Source File Inventory

| File | Assembly | Lines | Purpose |
|------|----------|-------|---------|
| `src/Koan.Data.Core/Schema/EntitySchemaGuard.cs` | Koan.Data.Core | 161 | Per-entity provisioning coordinator with state tracking, error caching, Singleflight |
| `src/Koan.Data.Core/Schema/ISchemaHealthContributor.cs` | Koan.Data.Core | 24 | Adapter contract for schema health and cache invalidation |
| `src/Koan.Data.Core/Schema/AggregateSchemaHealthContributor.cs` | Koan.Data.Core | 30 | Runtime bridge from generic interface to concrete adapter repository |
| `src/Koan.Core/Infrastructure/Singleflight.cs` | Koan.Core | 63 | Concurrent deduplication primitive with `ConcurrentDictionary<string, Lazy<Task>>` |
| `src/Koan.Data.Core/RepositoryFacade.cs` | Koan.Data.Core | 286 | Cross-cutting wrapper calling `EnsureHealthy()` before every data operation |
| `src/Koan.Data.Core/ServiceCollectionExtensions.cs` | Koan.Data.Core | -- | Registers `EntitySchemaGuard<,>` as singleton open generic |
| `src/Connectors/Data/Postgres/PostgresRepository.cs` | Koan.Data.Postgres | -- | Implements `ISchemaHealthContributor` for PostgreSQL DDL |
| `src/Connectors/Data/SqlServer/SqlServerRepository.cs` | Koan.Data.SqlServer | -- | Implements `ISchemaHealthContributor` for SQL Server DDL |
| `src/Connectors/Data/Sqlite/SqliteRepository.cs` | Koan.Data.Sqlite | -- | Implements `ISchemaHealthContributor` for SQLite DDL |
| `src/Connectors/Data/Mongo/MongoRepository.cs` | Koan.Data.Mongo | -- | Implements `ISchemaHealthContributor` for MongoDB collection/index |
| `src/Connectors/Data/Couchbase/CouchbaseRepository.cs` | Koan.Data.Couchbase | -- | Implements `ISchemaHealthContributor` for Couchbase scope/collection |

## Appendix B: Usage Scenarios

### Scenario 1: Normal Operation (Fast Path)

```
Request 1: Todo.Get(id)
  --> RepositoryFacade.Guard(ct)
  --> EntitySchemaGuard.EnsureHealthy(ct)
  --> _states["postgres:default:root"].IsProvisioned == false (first call)
  --> Singleflight.Run("postgres:default:root", ...)
  --> PostgresRepository.EnsureHealthy(ct) --> CREATE TABLE IF NOT EXISTS "todos" ...
  --> _states["postgres:default:root"] = ProvisionState(true, ...)
  --> actual query executes

Request 2: Todo.Get(otherId)  (same entity type, any time later)
  --> EntitySchemaGuard.EnsureHealthy(ct)
  --> _states["postgres:default:root"].IsProvisioned == true
  --> return immediately (fast path, no I/O)
```

### Scenario 2: Concurrent Requests (Singleflight)

```
Request A: Todo.Get(id1)   \
Request B: Todo.Get(id2)    |  all arrive before provisioning completes
Request C: Todo.Query(...)  /

Request A enters Singleflight.Run("postgres:default:root", ...)
  --> starts PostgresRepository.EnsureHealthy(ct)

Request B enters Singleflight.Run("postgres:default:root", ...)
  --> GetOrAdd returns same Lazy<Task> as Request A
  --> awaits same Task (no duplicate DDL)

Request C enters Singleflight.Run("postgres:default:root", ...)
  --> same: awaits existing Task

PostgresRepository.EnsureHealthy completes
  --> all three requests proceed to actual queries
  --> exactly one CREATE TABLE was issued
```

### Scenario 3: Provisioning Failure (Error Cache)

```
Request 1: Todo.Get(id) at T+0:00
  --> PostgresRepository.EnsureHealthy throws "permission denied"
  --> _states["postgres:default:root"] = ProvisionState(false, null, ProvisionError("permission denied", T+0:00, 1))
  --> caller receives exception

Request 2: Todo.Get(id) at T+1:30
  --> _states[key].Error age = 1:30 < 5:00
  --> throws "Provisioning failed... Retry in 03:30 (attempt #1)"
  --> no database contact

DBA grants CREATE TABLE permission at T+3:00

Request 3: Todo.Get(id) at T+3:00
  --> _states[key].Error age = 3:00 < 5:00
  --> throws "Retry in 02:00 (attempt #1)"  (still within window)

Request 4: Todo.Get(id) at T+5:01
  --> _states[key].Error age = 5:01 >= 5:00
  --> enters Singleflight, retries PostgresRepository.EnsureHealthy
  --> succeeds (permission now granted)
  --> _states[key] = ProvisionState(true, T+5:01, null)
  --> fast path for all future requests
```

### Scenario 4: Manual Recovery (ClearProvisioningError)

```
Request 1: Todo.Get(id) fails at T+0:00 (same as Scenario 3)

DBA grants permission at T+0:30
Operator calls guard.ClearProvisioningError() at T+0:30
  --> _states.TryRemove("postgres:default:root")

Request 2: Todo.Get(id) at T+0:31
  --> no cached state (cleared)
  --> enters Singleflight, provisions successfully
  --> no need to wait for 5-minute window
```

---

**End of Defensive Publication**

*This document is published as prior art to prevent patenting of the described techniques. The described system is implemented in the Koan Framework, an open-source .NET framework. This publication is intended to be available as prior art effective as of the date of disclosure.*
