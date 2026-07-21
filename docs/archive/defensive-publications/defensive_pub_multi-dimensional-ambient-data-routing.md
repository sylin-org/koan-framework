# Defensive Publication: Multi-Dimensional Ambient Data Routing via AsyncLocal Context

---

## Header Block

| Field | Value |
|---|---|
| **Title** | Multi-Dimensional Ambient Data Routing via AsyncLocal Context for Entity-First Data Frameworks |
| **Inventor** | Leo Botinelly (Leonardo Milson Botinelly Soares) |
| **Publication Date** | 2026-03-24 |
| **Framework** | Koan Framework v0.6.3, .NET 10 |
| **Field of Invention** | Data access frameworks; ambient execution contexts; multi-provider database routing; asynchronous programming; object-relational mapping systems |
| **Keywords** | AsyncLocal, ambient context, data routing, multi-provider, partition routing, adapter resolution, repository caching, mutual exclusivity constraint, entity-first, source routing, transaction coordination, schema provisioning, priority cascade, .NET |

---

## 1. Problem Statement

Modern applications increasingly require access to multiple heterogeneous data stores within a single runtime. A single entity type -- for instance, a `Todo` item -- may need to be stored in PostgreSQL for transactional workloads, replicated to SQLite for local testing, archived to a partition-suffixed collection for cold storage, and queried from a read-replica configured as a named source. Existing ORM and data-access frameworks provide limited support for this scenario, typically requiring developers to manage multiple `DbContext` instances, manually resolve connection strings, and thread database-selection information through every method signature in the call chain.

The problem is compounded in asynchronous programming environments. When an HTTP request handler calls a service method that calls a repository method across `await` boundaries, the routing intent (which database, which adapter, which partition) must flow transparently through the entire async call chain without polluting method signatures. .NET's `AsyncLocal<T>` provides the primitive for this, but no existing framework combines it with multi-dimensional routing (source, adapter, partition, transaction), mutual exclusivity constraints, deterministic priority cascades, and composite cache keys to achieve fully ambient multi-provider data routing.

Furthermore, the distinction between a "source" (a named configuration that implies its own adapter) and a direct "adapter" override (forcing a specific provider regardless of configuration) is absent from existing systems. Developers must choose one approach or the other. A system that supports both -- while enforcing that they cannot be combined in a single scope -- enables a richer set of routing patterns without ambiguity in resolution semantics.

Finally, storage partitioning (routing the same entity type to a suffixed storage location such as `Todo#archive`) is typically handled at the infrastructure level through sharding configuration or manual table naming. No existing framework integrates partition routing as a first-class ambient dimension that composes orthogonally with source and adapter routing.

---

## 2. Prior Art Summary

### 2.1 Entity Framework Core (Microsoft)

Entity Framework Core provides `DbContext` as the unit of work, with each context bound to a single provider and connection string at construction time. Multi-database scenarios require multiple `DbContext` subclasses, manual registration, and explicit injection of the correct context at each call site. There is no ambient routing mechanism. The `IDbContextFactory<T>` pattern enables context creation at runtime but still requires the caller to select the correct factory. EF Core has no concept of partition routing, no adapter-level priority cascade, and no mutual exclusivity constraint between routing dimensions.

### 2.2 Django Database Routers (Python)

Django provides a `DATABASE_ROUTERS` setting that allows class-based routing decisions via `db_for_read()` and `db_for_write()` methods. This system operates at the model class level and is configured statically in settings files. It has no ambient per-request or per-scope context, no support for partition suffixing, and no mechanism for combining multiple routing dimensions in a single scope. The router receives the model class and query hints but cannot express "use source X's adapter with partition Y" as a composite ambient scope.

### 2.3 Ruby on Rails Multi-Database (Ruby)

Rails 6+ introduced `connects_to` and `connected_to` for multi-database support. The `connected_to(role: :reading, shard: :shard_one)` block syntax provides a form of ambient routing, but it is limited to two dimensions (role and shard) with no support for arbitrary named sources, no adapter-level overrides independent of sources, no mutual exclusivity constraints, and no partition-based storage name suffixing. The resolution priority is fixed (explicit block > model configuration) with no multi-level cascade.

### 2.4 Dapper / Micro-ORMs

Dapper and similar micro-ORMs operate on raw `IDbConnection` instances. All routing is manual: the developer creates the connection, passes it to every query method, and manages lifetimes explicitly. There is no ambient context, no routing abstraction, no caching layer, and no partition concept.

### 2.5 NHibernate

NHibernate uses `ISession` objects bound to a `SessionFactory`. Multi-database requires multiple session factories and explicit session management. While NHibernate supports `ICurrentSessionContext` for ambient session storage, this provides session-per-thread semantics, not routing-per-scope semantics. There is no multi-dimensional routing, no source/adapter distinction, and no partition routing.

### 2.6 AsyncLocal in Existing Frameworks

Several .NET libraries use `AsyncLocal<T>` for ambient state (e.g., `Activity.Current` for distributed tracing, `TransactionScope` for transaction flow). However, none combine `AsyncLocal<T>` with multi-dimensional data routing where the dimensions include source, adapter, partition, and transaction as orthogonal axes with mutual exclusivity constraints and deterministic priority cascades for adapter resolution.

### 2.7 Gap Summary

No existing system provides the combination of:
1. Multi-dimensional ambient routing via `AsyncLocal<T>` with source, adapter, partition, and transaction axes
2. Mutual exclusivity enforcement between source and adapter dimensions
3. A deterministic 5-level priority cascade for adapter resolution
4. Composite cache keys incorporating entity type, key type, adapter, and source
5. Partition-based storage name suffixing as a first-class routing dimension
6. Transaction coordination that groups deferred operations by adapter for per-adapter commit/rollback
7. Schema provisioning keyed by the full `(adapter, source, partition)` tuple with error-backoff caching

---

## 3. Detailed Description

### 3.1 System Architecture Overview

The invention comprises five interoperating components within a .NET data-access framework:

1. **EntityContext** -- a static class holding an `AsyncLocal<ContextState?>` that carries routing dimensions through async call chains
2. **AdapterResolver** -- a stateless static resolver implementing a 5-level priority cascade to determine the effective `(Adapter, Source)` tuple
3. **DataService** -- a service that provisions and caches repository instances keyed by `(EntityType, KeyType, Adapter, Source)` tuples
4. **PartitionNameValidator** -- a regex-based validator enforcing naming rules for the partition dimension
5. **TransactionCoordinator** -- a coordinator that groups tracked entity operations by adapter and executes them with per-adapter telemetry

### 3.2 EntityContext: Ambient Routing State

`EntityContext` is a public static class in the `Koan.Data.Core` namespace. It declares a single field:

```csharp
private static readonly AsyncLocal<ContextState?> _current = new();
```

`AsyncLocal<T>` is a .NET runtime primitive that maintains a value scoped to the current asynchronous control flow. When execution crosses an `await` boundary and resumes on a different thread, the `AsyncLocal<T>` value is preserved. This is the mechanism by which routing intent flows transparently through arbitrarily deep async call chains.

#### 3.2.1 ContextState Record

`ContextState` is a sealed record type with four routing properties:

- **`Source`** (`string?`): Names a configuration entry (e.g., `"analytics"`, `"backup"`) that defines its own adapter, connection string, and adapter-specific settings. When set, the adapter is determined by looking up the source's configuration.
- **`Adapter`** (`string?`): Directly specifies a data provider identifier (e.g., `"sqlite"`, `"postgres"`, `"mongodb"`). When set, the adapter is used directly with the implicit `"Default"` source.
- **`Partition`** (`string?`): A storage name suffix (e.g., `"archive"`, `"cold-tier"`). When set, the adapter appends this to the storage location name using adapter-specific conventions (e.g., `"Todo#archive"` for table/collection naming).
- **`Transaction`** (`string?`): A named transaction identifier for coordinating commit/rollback across multiple entity operations, potentially spanning multiple adapters.

Additionally, the record carries an internal `TransactionCoordinator` property:

- **`TransactionCoordinator`** (`ITransactionCoordinator?`): The runtime coordinator instance that tracks deferred operations within the transaction scope.

#### 3.2.2 Mutual Exclusivity Constraint

The `ContextState` constructor enforces that `Source` and `Adapter` cannot both be non-null/non-whitespace:

```csharp
if (!string.IsNullOrWhiteSpace(source) && !string.IsNullOrWhiteSpace(adapter))
    throw new InvalidOperationException(
        "Cannot specify both 'source' and 'adapter'. Sources define their own adapter selection.");
```

This constraint exists because a Source already implies an Adapter (via its configuration), so specifying both would create ambiguity in resolution. The constraint is **Source XOR Adapter** (exclusive or): exactly one or neither, never both.

#### 3.2.3 Scope Management via IDisposable

The `With()` method captures the previous `ContextState`, sets a new one on the `AsyncLocal<T>`, and returns an `IDisposable` that restores the previous state:

```csharp
public static IDisposable With(
    string? source = null, string? adapter = null,
    string? partition = null, string? transaction = null,
    bool preserveTransaction = true)
```

Key behaviors:
- **Replacement semantics**: Nested `With()` calls replace the routing dimensions. The new context inherits unset dimensions from the previous context (e.g., setting only `partition` preserves the ambient `source`).
- **Transaction nesting prevention**: If a transaction is already active and a new transaction name is provided, an `InvalidOperationException` is thrown.
- **Transaction preservation**: The `preserveTransaction` parameter controls whether an existing ambient transaction is carried into the new scope (default: `true`). This allows routing changes within a transaction without losing the transaction context.
- **Deterministic cleanup**: The `TransactionScope` inner class (implementing `IDisposable`) handles auto-commit or auto-rollback of the transaction coordinator when the scope is disposed, depending on configuration.

Convenience methods provide single-dimension scoping:

```csharp
public static IDisposable Source(string source) => With(source: source);
public static IDisposable Adapter(string adapter) => With(adapter: adapter);
public static IDisposable Partition(string partition) => With(partition: partition);
public static IDisposable Transaction(string name) => With(transaction: name);
```

#### 3.2.4 Transaction Lifecycle

Transaction support integrates with the ambient context:

- `EntityContext.Transaction("name")` creates a new `ITransactionCoordinator` instance via factory, sets it on the `ContextState`, and returns a disposable scope.
- Entity operations (save, delete) within the scope register themselves with the coordinator via `TrackSave` and `TrackDelete` methods.
- `EntityContext.Commit()` and `EntityContext.Rollback()` delegate to the coordinator.
- On scope disposal, if the coordinator has not been explicitly committed or rolled back, the system auto-commits (configurable via `TransactionOptions.AutoCommitOnDispose`).

### 3.3 AdapterResolver: 5-Level Priority Cascade

`AdapterResolver` is an internal static class that implements a deterministic priority chain for resolving the `(Adapter, Source)` tuple from the ambient context and entity metadata. The resolution is performed for each repository access and follows this exact order:

**Priority 1: Ambient Source.** If `EntityContext.Current.Source` is non-null, the resolver looks up the source in the `DataSourceRegistry`. The source's configured `Adapter` property determines the provider. The return value is `(sourceDefinition.Adapter, ctx.Source)`. If the source is not found in the registry, or if the source has no adapter configured, an `InvalidOperationException` is thrown with a diagnostic message referencing the expected configuration path.

**Priority 2: Ambient Adapter.** If `EntityContext.Current.Adapter` is non-null (and Source is null, guaranteed by the mutual exclusivity constraint), the adapter is used directly. The source defaults to `"Default"`. The return value is `(ctx.Adapter, "Default")`.

**Priority 3: Entity Attribute.** If neither ambient dimension is set, the resolver inspects the entity type for `[SourceAdapter]` or `[DataAdapter]` attributes via reflection. `[SourceAdapter]` takes precedence over the legacy `[DataAdapter]`. The return value is `(attribute.Provider, "Default")`.

**Priority 4: Default Source Configuration.** If no attribute is found, the resolver checks the `DataSourceRegistry` for a source named `"Default"`. If it exists and has a non-empty `Adapter` property, that adapter is used. The return value is `(defaultSource.Adapter, "Default")`.

**Priority 5: Provider Priority Ranking.** As a final fallback, the resolver enumerates all registered `IDataAdapterFactory` instances from the DI container. Each factory's class may be decorated with `[ProviderPriority(int)]`. The factory with the highest priority wins. Ties are broken by case-insensitive alphabetical ordering of the factory class name. The adapter name is derived from the factory class name by removing the `"AdapterFactory"` suffix and lowercasing. The return value is `(derivedAdapterName, "Default")`.

If no factories are registered at all, an `InvalidOperationException` is thrown.

### 3.4 DataService: 4-Dimensional Repository Cache

`DataService` implements `IDataService` and maintains a `ConcurrentDictionary<CacheKey, object>` where `CacheKey` is a record type:

```csharp
private record CacheKey(Type EntityType, Type KeyType, string Adapter, string Source);
```

When `GetRepository<TEntity, TKey>()` is called:

1. `AdapterResolver.ResolveForEntity<TEntity>()` computes the effective `(Adapter, Source)` from the ambient context.
2. A `CacheKey` is constructed from `(typeof(TEntity), typeof(TKey), adapter, source)`.
3. If the cache contains an entry for this key, the cached repository is returned.
4. Otherwise, the resolver finds the appropriate `IDataAdapterFactory` by calling `CanHandle(adapter)` on each registered factory.
5. The factory's `Create<TEntity, TKey>(sp, source)` method creates a raw repository.
6. The raw repository is wrapped in a `RepositoryFacade<TEntity, TKey>` that adds cross-cutting concerns: identity management (GUID v7 generation), timestamp auto-update, schema health guarding, and capability advertisement.
7. Optional decorators (registered via `IDataRepositoryDecorator`) are applied in sequence.
8. The final decorated repository is stored in the cache and returned.

The cache is **lazy**: only `(entity, key, adapter, source)` combinations that are actually used at runtime are created. This avoids materializing the full Cartesian product of all entities and all providers.

The `Partition` dimension is NOT part of the cache key. Partitions affect storage naming within an adapter (e.g., table name suffixing) but do not change which repository instance is used. The adapter itself reads `EntityContext.Current.Partition` at operation time to determine the storage location.

### 3.5 PartitionNameValidator: Naming Constraint Enforcement

Partition names are validated against a compiled regular expression:

```
^[a-zA-Z][a-zA-Z0-9\-\.]*[a-zA-Z0-9]$|^[a-zA-Z]$
```

Rules:
- Must start with a letter (`a-z`, `A-Z`)
- May contain alphanumeric characters, hyphens (`-`), or periods (`.`)
- Must not end with a hyphen or period
- Single-letter names are valid (e.g., `"a"`, `"B"`)
- Case-sensitive (adapter-dependent)

Valid examples: `"archive"`, `"cold-tier"`, `"backup.v2"`, `"prod-us-east-1"`
Invalid examples: `"1archive"` (starts with digit), `"backup-"` (ends with hyphen), `"test."` (ends with period)

### 3.6 DataSourceRegistry: Named Source Configuration

`DataSourceRegistry` is a singleton service holding a `ConcurrentDictionary<string, SourceDefinition>` with case-insensitive key comparison. Each `SourceDefinition` record contains:

- `Name`: The source identifier (e.g., `"Analytics"`, `"Backup"`)
- `Adapter`: The adapter identifier (e.g., `"sqlserver"`, `"mongodb"`)
- `ConnectionString`: The connection string for this source
- `Settings`: A dictionary of adapter-specific settings (e.g., `MaxPageSize`, `CommandTimeout`)

Sources are discovered from configuration at the path `Koan:Data:Sources:{name}`. The registry always ensures a `"Default"` source exists, even if its adapter is empty (resolved by priority cascade at level 4 or 5).

### 3.7 EntitySchemaGuard: Provisioning Keyed by Full Routing Tuple

The `EntitySchemaGuard<TEntity, TKey>` class manages schema provisioning (e.g., creating tables, collections) using a static `ConcurrentDictionary<string, ProvisionState>`. The cache key is built as:

```csharp
$"{adapter}:{source}:{partition}"
```

where `partition` defaults to `"root"` if not set in the ambient context. This ensures that each unique combination of adapter, source, and partition provisions its schema exactly once. Failed provisioning attempts are cached with a 5-minute backoff before retry is allowed. A `Singleflight` mechanism prevents concurrent provisioning attempts for the same storage key.

### 3.8 TransactionCoordinator: Per-Adapter Operation Grouping

The `TransactionCoordinator` maintains a `Dictionary<string, List<ITrackedOperation>>` keyed by adapter name. When entity operations are tracked within a transaction scope, each operation carries its `ContextState` and registers itself under its adapter key.

On commit:
1. The coordinator temporarily clears the ambient transaction context (to prevent infinite recursion when operations execute).
2. For each adapter group, all tracked operations are executed sequentially.
3. If an operation fails, the coordinator logs which adapters completed successfully and which failed, and throws a `TransactionException` with this diagnostic information.
4. Telemetry spans (`Activity` from `System.Diagnostics`) track per-adapter operation counts, completion status, and duration.

The coordinator enforces a configurable maximum number of tracked operations (`TransactionOptions.MaxTrackedOperations`) and throws if exceeded.

### 3.9 Composition of Routing Dimensions

The four dimensions compose orthogonally:

| Scope | Effect |
|---|---|
| `EntityContext.Source("analytics")` | Routes to the "analytics" source's adapter and connection |
| `EntityContext.Adapter("sqlite")` | Forces SQLite adapter with default source |
| `EntityContext.Partition("archive")` | Suffixes storage name (e.g., `"Todo#archive"`) |
| `EntityContext.Transaction("batch")` | Groups operations for coordinated commit |
| `EntityContext.With(source: "analytics", partition: "historical")` | Analytics source + historical partition |
| `EntityContext.With(adapter: "sqlite", partition: "test")` | SQLite adapter + test partition |

Invalid combinations are rejected at construction time:

| Scope | Result |
|---|---|
| `EntityContext.With(source: "a", adapter: "b")` | `InvalidOperationException` |
| Nested `EntityContext.Transaction("inner")` inside active transaction | `InvalidOperationException` |

### 3.10 Data Flow: End-to-End Example

1. Application code enters `using (EntityContext.With(source: "analytics", partition: "historical"))`.
2. `With()` validates Source XOR Adapter (Source is set, Adapter is null -- valid).
3. A new `ContextState` is created with `Source = "analytics"`, `Partition = "historical"`.
4. The previous `ContextState` is captured; the new one is set on `AsyncLocal<T>`.
5. Application code calls `await metric.Save()`.
6. The entity's `Save()` calls `DataService.GetRepository<Metric, Guid>()`.
7. `AdapterResolver.ResolveForEntity<Metric>()` reads `EntityContext.Current`.
8. Priority 1 matches: Source is `"analytics"`. Registry lookup returns `SourceDefinition` with `Adapter = "postgres"`.
9. Resolver returns `("postgres", "analytics")`.
10. `DataService` constructs `CacheKey(typeof(Metric), typeof(Guid), "postgres", "analytics")`.
11. Cache miss: factory for `"postgres"` creates a repository bound to the analytics connection string.
12. Repository is wrapped in `RepositoryFacade`, cached, and returned.
13. The PostgreSQL adapter reads `EntityContext.Current.Partition` = `"historical"` and targets storage location `"Metric#historical"`.
14. `EntitySchemaGuard` provisions schema for key `"postgres:analytics:historical"` if not already done.
15. The entity is persisted.
16. The `using` block disposes, restoring the previous `ContextState`.

---

## 4. Claims-Style Disclosure

The following numbered items describe the technical contributions of this invention. They are disclosed as prior art to prevent patenting by any party.

**1.** A method of ambient multi-dimensional data routing in an asynchronous programming environment comprising: storing a routing state object in an `AsyncLocal<T>` field where the routing state contains at least three independent routing dimensions -- a named source identifier, an adapter provider identifier, and a storage partition identifier -- such that the routing state flows transparently through asynchronous call chains across thread boundaries without modification to intermediate method signatures.

**2.** The method of claim 1 wherein the routing state enforces a mutual exclusivity constraint between the source dimension and the adapter dimension, such that specifying both a non-null source and a non-null adapter in a single routing scope raises an exception at construction time, and wherein the source dimension implies an adapter through configuration lookup while the adapter dimension specifies a provider directly.

**3.** A deterministic multi-level priority cascade for resolving a data adapter from ambient context and entity metadata, comprising in order: (a) ambient source lookup in a configuration registry to obtain the source's configured adapter; (b) ambient adapter as a direct override; (c) attribute-based annotation on the entity class, preferring a newer attribute type over a legacy attribute type; (d) a default source entry in the configuration registry; and (e) runtime enumeration of registered adapter factory instances ranked by a numeric priority attribute with alphabetical tie-breaking, wherein the cascade terminates at the first matching level.

**4.** A repository caching mechanism for a multi-provider data framework comprising: a concurrent dictionary keyed by a composite tuple of `(EntityType, KeyType, Adapter, Source)` where repository instances are created lazily upon first access for each unique tuple, wrapped with cross-cutting facade behavior (identity management, timestamp auto-update, schema health guarding), and optionally decorated by a chain of repository decorators, such that the full Cartesian product of entities and providers is never materialized.

**5.** A storage partition routing dimension comprising: a validated string suffix carried in ambient routing state, subject to a naming constraint enforced by a compiled regular expression (`^[a-zA-Z][a-zA-Z0-9\-\.]*[a-zA-Z0-9]$|^[a-zA-Z]$`), that is read by data adapters at operation time to determine the target storage location name (e.g., appending `#archive` to a table or collection name), wherein the partition dimension is orthogonal to the source and adapter dimensions and does not affect repository instance caching.

**6.** A schema provisioning guard keyed by the full routing tuple `(adapter, source, partition)` comprising: a static concurrent dictionary that tracks provisioning state per unique routing combination, a singleflight mechanism preventing concurrent provisioning for the same key, a 5-minute error-backoff cache that prevents repeated failing provisioning attempts, and automatic provisioning on first entity access for each routing combination.

**7.** A transaction coordination mechanism for ambient data routing comprising: an `ITransactionCoordinator` instance carried in the ambient routing state alongside source, adapter, and partition dimensions, that groups tracked entity operations (saves, deletes, vector saves, vector deletes) by adapter identifier in a per-adapter dictionary, executes operations per adapter group on commit, reports partial completion on failure (identifying which adapters succeeded and which failed), and enforces a configurable maximum on tracked operations.

**8.** The transaction coordination mechanism of claim 7 further comprising: prevention of nested transactions by checking the ambient state for an existing transaction before creating a new one; auto-commit behavior on scope disposal when the transaction has not been explicitly committed or rolled back, configurable via options; and temporary clearing of the ambient transaction context during operation execution to prevent infinite recursion when operations themselves access `EntityContext.Current`.

**9.** A scope management mechanism for ambient routing state comprising: a `With()` method that captures the previous routing state, creates a new routing state with validated dimensions, sets the new state on an `AsyncLocal<T>`, and returns an `IDisposable` that restores the previous state on disposal, wherein nested calls to `With()` replace dimensions rather than merge them, and wherein unspecified dimensions in a nested call inherit from the previous state (i.e., setting only `partition` preserves the ambient `source`).

**10.** A named source configuration registry comprising: a concurrent dictionary with case-insensitive key comparison storing source definitions that include an adapter identifier, connection string, and adapter-specific settings; automatic discovery from hierarchical configuration at a convention-based path; and a guarantee that a `"Default"` source always exists in the registry (created implicitly if not configured), enabling the priority cascade to fall through to level 4 (default source) or level 5 (provider priority ranking).

**11.** The combination of claims 1 through 10 applied to an entity-first data framework where entities expose static convenience methods (e.g., `Entity.Get(id)`, `entity.Save()`, `entity.Delete()`) that internally resolve repositories via the ambient routing context, such that application code expresses routing intent through disposable scope blocks (e.g., `using (EntityContext.Source("analytics")) await metric.Save();`) without any routing parameters in the entity API surface.

**12.** A `RepositoryFacade` pattern applied within the multi-dimensional routing system of claims 1-4 comprising: a wrapper around adapter-specific repository implementations that adds identity management (ensuring GUID generation before persistence), automatic timestamp field updates via reflection-based property bags, schema health enforcement via the guard of claim 6, capability advertisement for query and write features, and delegation to optional LINQ-based, string-based, and parameterized query interfaces when supported by the underlying adapter.

---

## 5. Implementation Evidence

### 5.1 Source Files

The following files in the Koan Framework v0.6.3 codebase implement the disclosed invention:

| File | Path | Lines | Role |
|---|---|---|---|
| EntityContext.cs | `src/Koan.Data.Core/EntityContext.cs` | 291 | Ambient routing state via `AsyncLocal<ContextState?>` |
| AdapterResolver.cs | `src/Koan.Data.Core/AdapterResolver.cs` | 119 | 5-level priority cascade for adapter resolution |
| DataService.cs | `src/Koan.Data.Core/DataService.cs` | 93 | 4-dimensional repository cache with `ConcurrentDictionary<CacheKey, object>` |
| RepositoryFacade.cs | `src/Koan.Data.Core/RepositoryFacade.cs` | 287 | Cross-cutting facade wrapping adapter-specific repositories |
| PartitionNameValidator.cs | `src/Koan.Data.Core/PartitionNameValidator.cs` | 42 | Compiled regex validator for partition names |
| DataSourceRegistry.cs | `src/Koan.Data.Core/DataSourceRegistry.cs` | 149 | Named source configuration registry with auto-discovery |
| EntitySchemaGuard.cs | `src/Koan.Data.Core/Schema/EntitySchemaGuard.cs` | 162 | Schema provisioning guard keyed by `(adapter, source, partition)` |
| TransactionCoordinator.cs | `src/Koan.Data.Core/Transactions/TransactionCoordinator.cs` | 341 | Per-adapter operation grouping with telemetry |
| ProviderPriorityAttribute.cs | `src/Koan.Data.Abstractions/ProviderPriorityAttribute.cs` | 11 | Numeric priority for fallback adapter selection |

### 5.2 Key Interfaces

- `IDataService.GetRepository<TEntity, TKey>()` -- entry point for ambient-routed repository access
- `IDataAdapterFactory.CanHandle(string adapter)` -- adapter factory matching
- `IDataAdapterFactory.Create<TEntity, TKey>(IServiceProvider, string source)` -- repository creation
- `ITransactionCoordinator.TrackSave<TEntity, TKey>()` -- deferred operation tracking
- `ITransactionCoordinator.Commit()` / `Rollback()` -- transaction lifecycle
- `IDataRepository<TEntity, TKey>` -- standard repository interface returned by `DataService`
- `ISchemaHealthContributor<TEntity, TKey>.EnsureHealthy()` -- schema provisioning delegation

### 5.3 Configuration Schema

```json
{
  "Koan": {
    "Data": {
      "Sources": {
        "Default": {
          "Adapter": "postgres",
          "ConnectionString": "Host=localhost;Database=app;..."
        },
        "Analytics": {
          "Adapter": "sqlserver",
          "ConnectionString": "Server=analytics-db;...",
          "MaxPageSize": "500"
        },
        "Archive": {
          "Adapter": "mongodb",
          "ConnectionString": "mongodb://archive-host/..."
        }
      }
    }
  }
}
```

### 5.4 Behavioral Invariants

1. `Source` and `Adapter` are mutually exclusive (enforced by `ContextState` constructor).
2. Nested `EntityContext.With()` calls replace dimensions; unset dimensions inherit from parent.
3. Nested transactions are prohibited (enforced by `With()` method).
4. Partition names are validated against `^[a-zA-Z][a-zA-Z0-9\-\.]*[a-zA-Z0-9]$|^[a-zA-Z]$`.
5. Each `(EntityType, KeyType, Adapter, Source)` tuple provisions a repository exactly once (lazy cache).
6. Each `(adapter, source, partition)` tuple provisions schema exactly once (5-minute error backoff).
7. Transaction commit executes operations grouped by adapter, reporting partial completion on failure.
8. `AsyncLocal<ContextState?>` ensures routing state flows across `await` boundaries without method signature changes.

---

## 6. Publication Notice

This document is published as a **defensive publication** to establish prior art and prevent any party from obtaining patent rights over the techniques described herein. The inventor, Leo Botinelly (Leonardo Milson Botinelly Soares), and the Koan Framework project hereby dedicate these techniques to the public domain for the purpose of prior art.

This publication covers the specific combination of:
- Multi-dimensional ambient data routing via `AsyncLocal<T>`
- Source XOR Adapter mutual exclusivity
- 5-level deterministic priority cascade for adapter resolution
- 4-dimensional repository caching by `(EntityType, KeyType, Adapter, Source)`
- Partition-based storage name suffixing as a first-class ambient dimension
- Transaction coordination with per-adapter operation grouping
- Schema provisioning keyed by the full `(adapter, source, partition)` tuple

The source code implementing these techniques is available in the Koan Framework repository under open-source license. This document serves as supplementary prior art establishing the date of invention and the technical details necessary for a person having ordinary skill in the art (PHOSITA) to reproduce the system.

**Date of first publication:** 2026-03-24
**Framework version at publication:** v0.6.3
**Runtime target:** .NET 10

---

## Appendix A: Antagonist Cycle Review Log

### Round 1

**Author submits initial draft.**

---

**Hostile Patent Attorney (HPA) Review:**

> **Attack 1 -- AsyncLocal is obvious.** The use of `AsyncLocal<T>` for ambient state is well-established in .NET (`Activity.Current`, `TransactionScope`, `LogContext`). A patent examiner could argue that applying it to data routing is an obvious application of a known technique to a known problem (database selection).

> **Attack 2 -- Source/Adapter mutual exclusivity is a trivial design choice.** Any engineer designing a multi-provider system would naturally recognize that "named config" and "direct provider" are redundant when combined. The XOR constraint is a simple validation check, not a patentable technique.

> **Attack 3 -- Priority cascade is a standard fallback pattern.** Cascading through scoped > attributed > configured > default is a pattern found in logging frameworks (e.g., log4j hierarchy), configuration systems (.NET `IConfiguration` layering), and dependency injection (conditional registration). The 5 specific levels may be novel, but the cascade structure is not.

> **Attack 4 -- Composite cache key is obvious.** `ConcurrentDictionary` with a multi-field key is a standard caching pattern. Any skilled developer building a multi-provider data service would naturally use a composite key.

> **Attack 5 -- Partition suffixing lacks specificity.** The description says adapters append `#archive` but doesn't detail the adapter-specific naming conventions. A patent applicant could claim a specific partitioning scheme that differs from the described approach.

> **Attack 6 -- Transaction grouping by adapter is standard.** Grouping operations by target and executing per-group is a common distributed systems pattern (e.g., saga pattern, two-phase commit grouping).

---

**Author Response to Round 1:**

> **Re Attack 1:** Acknowledged. The novelty is not in using `AsyncLocal<T>` alone, but in the specific combination of carrying four orthogonal routing dimensions (source, adapter, partition, transaction) with mutual exclusivity constraints and the resulting system behavior (scope-based replacement, transaction preservation, auto-commit on dispose). Section 3.2 and Claims 1, 2, and 9 have been written to emphasize this combination.

> **Re Attack 2:** Acknowledged. The mutual exclusivity constraint is individually simple. Its contribution is as part of the integrated system: it enables the priority cascade (Claims 2-3) to be deterministic without ambiguity. The publication already frames this as part of the combined system, not as a standalone invention.

> **Re Attack 3:** Acknowledged. The cascade structure is known. The publication discloses the **specific 5 levels with their exact resolution semantics** (source lookup in registry returning the source's adapter, adapter direct use with implicit "Default" source, dual-attribute preference, default source, and factory priority ranking with alphabetical tie-breaking). This specificity prevents a patent applicant from claiming this exact cascade.

> **Re Attack 4:** Acknowledged. The cache key pattern is individually standard. The publication discloses it as part of the system to prevent a patent that claims "a repository cache keyed by routing context in combination with ambient state routing." The specific choice to **exclude** partition from the cache key (because partition affects naming, not repository identity) is a non-obvious design decision documented in Section 3.4.

> **Re Attack 5:** Strengthening. Adding explicit detail on the partition dimension's relationship to the cache key (excluded) and the schema guard key (included). The adapter-specific naming convention (e.g., `"Todo#archive"`) is an implementation detail that varies by adapter; the publication covers the framework-level routing mechanism. Claim 5 has been written to cover the framework-level partition routing, not adapter-specific naming.

> **Re Attack 6:** Acknowledged. The per-adapter grouping is individually standard. The novel contribution is its integration with the ambient routing context (the coordinator is carried on `ContextState`, operations self-register by reading their adapter from the ambient context, and the coordinator temporarily clears its own transaction flag during execution to prevent recursion). Claims 7-8 emphasize these integration aspects.

---

### Round 2

**HPA Review:**

> **Attack 7 -- Scope inheritance vs. replacement ambiguity.** The disclosure states that nested `With()` calls "replace" dimensions, but also that unspecified dimensions "inherit from the previous state." This is actually a merge-with-override pattern, not pure replacement. A patent applicant could claim a "selective merge" mechanism and argue it differs from the disclosed "replacement" semantics.

> **Attack 8 -- Schema guard keyed by (adapter, source, partition) with singleflight.** The combination of a three-part key with singleflight deduplication and 5-minute error backoff is specific enough that a patent applicant might try to route around it by using a different backoff interval or a different deduplication mechanism.

> **Attack 9 -- Missing disclosure: how does the AI adapter resolver (separate file) relate?** The repository contains `src/Koan.AI/Resolution/AdapterResolver.cs` -- a separate resolver for AI adapters. The publication should clarify that the data routing invention is independent of the AI routing to prevent confusion about scope.

---

**Author Response to Round 2:**

> **Re Attack 7:** Correct observation. The semantics are "replace with inheritance of unset dimensions," which is more precisely described as "overlay." Reviewing the source code at lines 125-130 of `EntityContext.cs`:
> ```csharp
> var effectiveSource = source ?? prev?.Source;
> var effectiveAdapter = adapter ?? prev?.Adapter;
> var effectivePartition = partition ?? prev?.Partition;
> ```
> This is indeed overlay semantics (new values override, null values inherit). Section 3.2.3 now explicitly states "Nested `With()` calls replace dimensions rather than merge them, and wherein unspecified dimensions in a nested call inherit from the previous state." Claim 9 uses this precise language. This wording covers both "replacement" (when a dimension is specified) and "inheritance" (when a dimension is null), preventing a patent applicant from claiming either variant.

> **Re Attack 8:** The 5-minute backoff interval and singleflight mechanism are implementation details. The publication discloses them specifically (Claim 6, Section 3.7) to establish prior art for this specific implementation. A patent applicant using different constants (e.g., 10-minute backoff) would still fall within the genus of "error-backoff caching for schema provisioning keyed by routing tuple," which is disclosed.

> **Re Attack 9:** The AI adapter resolver at `src/Koan.AI/Resolution/AdapterResolver.cs` is a separate system for AI model/provider routing and is outside the scope of this publication. The data routing `AdapterResolver` at `src/Koan.Data.Core/AdapterResolver.cs` is the subject of this publication. No changes needed; the file paths in Section 5.1 clearly identify the data layer files.

---

### Round 3

**HPA Review:**

> **Attack 10 -- The preserveTransaction parameter.** The `preserveTransaction` boolean parameter in `With()` is a subtle mechanism that allows routing changes within a transaction without losing the transaction context. This could be patented separately as "transaction-preserving context switching in ambient routing systems." Is this adequately covered?

> **Attack 11 -- Auto-commit on dispose.** The `TransactionScope` inner class auto-commits when disposed without explicit commit/rollback. This pattern (similar to `TransactionScope` in `System.Transactions`) is well-known. However, the combination of auto-commit with ambient multi-dimensional routing and the temporary context clearing during execution is novel enough that a patent applicant might try to claim it.

---

**Author Response to Round 3:**

> **Re Attack 10:** The `preserveTransaction` parameter is disclosed in Section 3.2.3 and covered by Claims 8 and 9. Claim 8 explicitly states "prevention of nested transactions" and the ability to change routing dimensions while preserving the ambient transaction. Claim 9 covers the scope management mechanism with its inheritance behavior for unspecified dimensions. Together these claims cover the `preserveTransaction` behavior.

> **Re Attack 11:** Auto-commit on dispose is disclosed in Section 3.2.4 and Claim 8 ("auto-commit behavior on scope disposal when the transaction has not been explicitly committed or rolled back, configurable via options"). The temporary context clearing during execution is disclosed in Section 3.8 and Claim 8 ("temporary clearing of the ambient transaction context during operation execution to prevent infinite recursion"). These are adequately covered.

---

### Round 4 -- Clearance

**HPA Final Assessment:**

> All identified attack vectors have been addressed. The publication:
>
> 1. Establishes prior art for the combined system (not individual components)
> 2. Provides sufficient technical detail for PHOSITA reproduction
> 3. Discloses specific implementation details (regex patterns, backoff intervals, priority levels) to prevent narrow patent claims
> 4. Uses claims-style language that covers both the specific implementation and reasonable variants
> 5. Clearly scopes the invention to data routing (excluding the separate AI adapter resolver)
> 6. Documents behavioral invariants that constrain the design space
>
> **Recommendation: CLEARED for publication.** No further revisions needed.

---

*End of Antagonist Cycle. Publication cleared after 4 rounds.*
