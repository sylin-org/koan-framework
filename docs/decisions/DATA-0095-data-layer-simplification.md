---
id: DATA-0095
slug: DATA-0095-data-layer-simplification
domain: DATA
status: Accepted
date: 2026-05-17
---

# ADR 0095: Data-layer simplification — collapse five abstraction chains

## Context

The DATA-0092 / DATA-0093 / DATA-0094 work hardened the data layer end-to-end across eight adapters (384 specs, zero fail). It also surfaced patterns where the framework's ceremony exceeds the work being done — repeatedly, in shapes that produce the same bug class across adapter implementations.

Five specific findings drove this ADR:

1. **`IDataRepositoryWithOptions.Query(object?, options, ct)` silently ignored its `query` parameter on every adapter that didn't dispatch on `Expression<>`.** The bug was found in Sqlite, Postgres, SqlServer, Json, Redis, and Couchbase — six adapters, identical bug, all silent. The untyped `object?` made every adapter responsible for the same `switch(query.GetType())` and gave the compiler no way to enforce it. Fixes in `5697c351`, `56929107` plugged each instance individually but the shape that lets the bug recur is still there.

2. **`INamingProvider` is four concepts for one question.** `GetStorageName` + `GetConcretePartition` + `RepositorySeparator` + (newly added in 56929107) `UsesNativePartitionContainer`, all coordinated by `NamingComposer`. The new flag was needed because Couchbase scopes don't fit the suffix model; the existence of the flag is a smell that the original abstraction split was wrong.

3. **Schema readiness goes through five layers.** `EntitySchemaGuard<T,K>` (process-wide static, 5-min backoff) → `Singleflight.Run` → `ISchemaHealthContributor<T,K>` → adapter `EnsureHealthy` → `GetCollectionContext` → `EnsureCollection`. The contributor interface adds zero logic beyond forwarding to the adapter. The static state caused cross-spec-class test pollution and forced a `ResetAll()` escape hatch (`098614e8`).

4. **`AppHost.Current` is a process-wide static for ambient `IServiceProvider` lookup.** It's how `Entity<T>.Get(id)` and friends resolve their backing repository without taking an IServiceProvider parameter. Tests have to manually assign it per spec class; it pins ServiceProvider references across test boundaries (cause of the `AggregateConfigs.Reset()` patch in `098614e8`). It's the wrong scope (process) for what it represents (request / test / job context).

5. **Relational DDL chain has five layers for ~30 lines of logic.** `IRelationalDdlExecutor` + `IRelationalSchemaOrchestrator` + `IRelationalStoreFeatures` + per-adapter `EnsureOrchestrated` + `Execute(Instruction("data.ensureCreated"))`. The orchestrator's value is choosing between Json / Projections / Physical materialization (one switch statement). The chain produced the `AddRelationalOrchestration()` missing-from-auto-registrar bug class fixed in `4c04a313`.

## Decision

Collapse all five chains. Split into two phases by scope:

- **Phase 1** — cross-cutting framework changes (#1–#4). Touches every adapter. Greenfield-acceptable per project mandate; third-party adapters update with us.
- **Phase 2** — relational-only consolidation (#5). Localized to the relational adapter family.

### Phase 1

#### 1.1 Typed `Query` overloads + marker interfaces

Replace the untyped `Query(object?, options, ct)` slot with capability-marked interfaces. Compiler enforces dispatch.

```csharp
// Required of every data repository
public interface IDataRepository<TEntity, TKey> { /* CRUD basics */ }

// Required of every queryable adapter (i.e. every adapter the matrix exercises)
public interface IPredicateQueryRepository<TEntity, TKey> : IDataRepository<TEntity, TKey>
{
    Task<RepositoryQueryResult<TEntity>> Query(
        Expression<Func<TEntity, bool>>? predicate,
        DataQueryOptions? options,
        CancellationToken ct = default);
}

// Optional: opt-in for adapters that accept raw provider-string queries
public interface IStringQueryRepository<TEntity, TKey> : IDataRepository<TEntity, TKey>
{
    Task<RepositoryQueryResult<TEntity>> Query(
        string statement,
        DataQueryOptions? options,
        CancellationToken ct = default);
}

// Optional: opt-in for adapters with a structured provider query type
public interface IProviderQueryRepository<TEntity, TKey, TQuery> : IDataRepository<TEntity, TKey>
{
    Task<RepositoryQueryResult<TEntity>> Query(
        TQuery query,
        DataQueryOptions? options,
        CancellationToken ct = default);
}
```

`Data<TEntity, TKey>.QueryWithCount` dispatches based on input type:

```csharp
public static Task<QueryResult<TEntity>> QueryWithCount(
    Expression<Func<TEntity, bool>>? predicate,
    DataQueryOptions? options = null,
    CancellationToken ct = default) // typed entry point
{
    var repo = Repo; // already typed as IPredicateQueryRepository<TEntity, TKey>
    return ApplyOrchestration(repo.Query(predicate, options, ct), options);
}

public static Task<QueryResult<TEntity>> QueryWithCount(
    string statement,
    DataQueryOptions? options = null,
    CancellationToken ct = default)
{
    if (Repo is not IStringQueryRepository<TEntity, TKey> sq)
        throw new NotSupportedException($"{typeof(TEntity).Name}'s adapter does not support string queries.");
    return ApplyOrchestration(sq.Query(statement, options, ct), options);
}
```

Net change: deletes `IDataRepositoryWithOptions.Query(object?, options, ct)`. Every adapter migrates its `Query(object?)` implementation to `IPredicateQueryRepository.Query(predicate)`. SQL adapters add `IStringQueryRepository`. The six-adapter silent-degrade bug becomes unrepresentable.

#### 1.2 `INamingProvider` collapse

Replace four members + `NamingComposer` + `StorageNameRegistry` cache with one method per adapter, owned end-to-end by the adapter:

```csharp
public interface INamingProvider
{
    string Provider { get; }
    string ResolveStorage(Type entityType, string? partition, IServiceProvider services);
}
```

Adapter is now wholly responsible for storage resolution — `[StorageName]` precedence, partition suffix or native container, sanitization, identifier limits. Each adapter caches its own results in whatever scope makes sense (per-IServiceProvider preferred — see 1.4).

`StorageNameRegistry`, `NamingComposer`, `GetConcretePartition`, `RepositorySeparator`, `UsesNativePartitionContainer` all delete. The DATA-0094 flag is subsumed.

#### 1.3 Schema readiness collapse

Replace the five-layer chain with one method on the repository:

```csharp
public interface IDataRepository<TEntity, TKey>
{
    /// Idempotent. Adapter implements its own retry / caching / native-primitive provisioning.
    Task EnsureReady(CancellationToken ct = default);
    /* ... existing CRUD ... */
}
```

`EntitySchemaGuard<T,K>`, `ISchemaHealthContributor<T,K>`, `GuardStateRegistry`, the static `_states` cache, and `EntitySchemaGuard.ResetAll()` all delete. Adapters that need single-flight import `Singleflight` directly (already a primitive in `Koan.Core.Singleflight`). The 5-minute backoff moves into the adapter that wants it — most don't.

Repository facade calls `await EnsureReady(ct)` once before any operation that needs the schema (writes, queries). Cached per-repo-instance, which is request-scoped in ASP.NET, so the cache is naturally short-lived and bound to the right scope.

#### 1.4 `AppHost.Current` → `AsyncLocal<IServiceProvider>`

The ambient service-provider lookup that powers `Entity<T>.Get(id)` static methods becomes flow-scoped:

```csharp
public static class AppHost
{
    private static readonly AsyncLocal<IServiceProvider?> _current = new();
    public static IServiceProvider? Current
    {
        get => _current.Value;
        set => _current.Value = value;
    }
}
```

ASP.NET seeds `_current` at the start of each request (already done via middleware). Test factories seed it per spec via `IClassFixture` (already done via base class). Background jobs / console hosts seed it explicitly at startup (one-line change in `AddKoan()` for the default case).

The visible change is zero for happy-path callers; the elimination of the cross-test pollution bug class is the win. `AggregateConfigs.Reset()` and `EntitySchemaGuard.ResetAll()` patches both delete because the state is no longer shared.

### Phase 2

#### 2.1 Relational DDL chain consolidation

Merge the orchestrator's logic into a single per-adapter `EnsureSchema` method. Drop `IRelationalSchemaOrchestrator`, `IRelationalDdlExecutor`, `IRelationalStoreFeatures` as separate abstractions:

```csharp
// In SqliteRepository / PostgresRepository / SqlServerRepository
protected override Task EnsureSchema(Type entityType, CancellationToken ct)
{
    // The ~30 lines that pick Json / Projections / Physical materialization and emit DDL.
    // Adapter-specific connection, dialect, identifier quoting all stay local — they were
    // local before, just expressed through the executor interface.
}
```

The relational base class (a new thin abstract class, not the existing orchestrator) provides shared helpers for column-list construction and projection metadata. Each adapter wires its own connection / dialect / identifier-quoting inline rather than going through the executor indirection.

Net change: drops three interfaces and the orchestrator singleton. The `services.AddRelationalOrchestration()`-missing-from-auto-registrar bug class is eliminated because there's no orchestrator to forget to register. ~30 lines duplicated across three SQL adapters is the cost.

## Consequences

### Positive

- **Six adapter bug classes become unrepresentable.** Typed query overloads, repo-owned readiness, and adapter-owned naming all enforce correctness in the type system or in the adapter's own scope — silent degrade is impossible.
- **Cross-test pollution disappears.** `AsyncLocal` + per-repo readiness caching = no process-wide static state for the test framework to fight. `Reset()` escape hatches all delete.
- **One concept, one place.** `Query` lives on typed interfaces. Naming lives on the adapter. Readiness lives on the repo. DDL lives on the relational adapter base. Five fewer "where does this logic live?" lookups per change.
- **The DATA-0094 `UsesNativePartitionContainer` flag deletes.** Couchbase still handles partitions natively, but the logic lives in `CouchbaseAdapterFactory.ResolveStorage` where it always belonged — the framework doesn't need a flag to be told to stay out of the way.

### Negative

- **Migration cost on third-party adapters.** Every adapter outside the framework repo updates. Acceptable per the greenfield mandate but real.
- **~30 lines of relational DDL duplicated across Postgres / SqlServer / Sqlite (Phase 2).** Real cost. The abstraction it replaces shipped without saving more code than it added — the duplication is preferable.
- **Adapters lose framework-provided readiness backoff / singleflight.** Adapters that want them import `Singleflight` directly and roll their own backoff. Three lines of code per adapter that wants it; zero for adapters that don't.

### Neutral

- **`IOptions<T>` binding for adapter config** is a separate cleanup not in this ADR. Listed as a future tightening — replaces `AdapterOptionsConfigurator` manual key-by-key reading with standard `services.Configure<TOptions>(...)` patterns. Out of scope here.

## Implementation plan

Phase 1 sequence: `#4 → #1 → (#2 + #3)`.

| Step | Item | Touches | Risk |
|---|---|---|---|
| Phase 1a | #4 AppHost → AsyncLocal | Internal-only | Lowest — no API change |
| Phase 1b | #1 Typed Query overloads | All adapters + orchestrator | Mechanical migration |
| Phase 1c | #2 + #3 INamingProvider + readiness collapse | All adapters + StorageNameRegistry + EntitySchemaGuard + ISchemaHealthContributor | Highest within Phase 1 — but the overlap (storage caching moves with readiness state into the repo) means doing them together is one pass instead of two breaking changes |
| Phase 2 | #5 Relational DDL consolidation | SQL adapters only | Isolated — Phase 1 unblocks but doesn't gate |

Each step delivers a green matrix before the next begins. The 384-spec AdapterSurface suite is the migration safety net.

## Alternatives considered

**Alt 1 — Add `[Obsolete]` compat shims for one version.** Surfaces both old and new for one cycle. Doubles the interface surface during the migration window. **Rejected** — the project is greenfield-acceptable for this branch; the cleanup payoff is much bigger than the migration cost on third-party adapters.

**Alt 2 — Keep `Query(object?, options, ct)` and add runtime capability check.** Same shape as today, but the orchestrator validates inputs against a `Capabilities` enum before calling the adapter. Stops the silent-degrade but keeps the untyped contract. **Rejected** — the bug class is still representable; the compiler should enforce, not the runtime.

**Alt 3 — Keep `EntitySchemaGuard` but scope it to `IServiceProvider`.** Drops the process-wide static cache; preserves the contributor interface. Smaller change but keeps the five-layer chain. **Rejected** — the contributor interface adds zero logic, the orchestrating guard is unnecessary if the adapter does its own once-per-repo cache.

**Alt 4 — Inline relational DDL per-adapter without a base class.** Even thinner than the Phase 2 proposal. Avoided because shared projection-metadata helpers are non-trivial and worth a base class. **Considered, rejected** — the base class earns its keep; the orchestrator + executor + features triple does not.
