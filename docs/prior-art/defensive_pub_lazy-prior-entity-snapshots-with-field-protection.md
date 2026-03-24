# Defensive Patent Publication

## Entity Lifecycle Event Pipeline with Lazy Prior-State Loading and Field-Level Mutation Protection

**Publication Type:** Defensive Patent Publication (prior art disclosure)
**Inventor:** Leo Botinelly (Leonardo Milson Botinelly Soares)
**Date of Disclosure:** 2026-03-24
**Framework:** Koan Framework v0.6.3 (.NET, target net10.0)
**Repository:** github.com/koan-framework (Koan.Data.Core assembly)

---

## 1. Abstract

This disclosure describes a system and method for intercepting entity persistence operations (load, upsert, remove) through a multi-phase lifecycle event pipeline that provides: (a) lazy-loaded prior entity state that defers database retrieval until explicitly accessed by a hook, (b) field-level mutation protection that captures property value snapshots after a setup phase and validates them at enforcement boundaries before and after persistence, (c) per-entity cancellation with structured reason codes within batch operations, (d) batch disposition tracking that classifies aggregate outcomes as Success, PartialSuccess, or Cancelled, and (e) atomic batch semantics where any single entity's cancellation can abort the entire batch. The pipeline is statically registered per entity type with zero-allocation fast paths when no hooks are configured, and hooks execute against a shared context that flows a mutable current-entity reference through ordered phases: Setup, Before, Persist, After.

---

## 2. Technical Problem

Object-relational mapping (ORM) frameworks and entity persistence libraries commonly provide lifecycle callbacks (e.g., Rails ActiveRecord `before_save`/`after_save`, Django signals `pre_save`/`post_save`, JPA `@PrePersist`/`@PostPersist`, Entity Framework Core `SaveChanges` interception). These existing systems suffer from several deficiencies when applied to real-world domain logic:

**Problem 1 -- Eager prior-state loading.** When a before-save hook needs to compare the incoming entity against its previously persisted version, existing frameworks either (a) do not provide prior state at all (Rails, Django, Spring JPA), forcing the developer to issue a manual database query inside every hook, or (b) provide it eagerly through a change tracker (EF Core), which loads the prior state for every save operation regardless of whether any hook inspects it. In write-heavy systems with many entity types, only a fraction of which have hooks that inspect prior state, eager loading imposes unnecessary I/O and memory overhead.

**Problem 2 -- No field-level mutation protection.** Once an entity enters a lifecycle pipeline, any hook in the chain can mutate any property. There is no mechanism to declare that certain fields (e.g., `CreatedAt`, `TenantId`, `OwnerId`) must remain immutable throughout the pipeline while still allowing controlled mutation of other fields (e.g., `UpdatedAt`, `Slug`). Developers must rely on code review discipline rather than runtime enforcement.

**Problem 3 -- Coarse-grained batch outcomes.** Existing ORMs treat batch persistence as all-or-nothing. When lifecycle hooks cancel individual entities within a batch, there is no structured mechanism to report which entities succeeded, which were cancelled, and why -- nor to let domain logic decide whether partial success is acceptable or the entire batch must be aborted.

**Problem 4 -- Hook overhead on unregistered entities.** Most frameworks evaluate lifecycle hook infrastructure on every operation even when no hooks are registered for a given entity type, adding unnecessary method calls, delegate invocations, and context object allocations to hot persistence paths.

---

## 3. Solution

The disclosed system introduces an entity lifecycle event pipeline implemented as a set of cooperating static generic classes in .NET. The pipeline architecture consists of the following components and behaviors:

### 3.1 Pipeline Phases

Every entity persistence operation (Load, Upsert, Remove) passes through up to five ordered phases:

1. **Setup** -- Initialization hooks that configure the execution context. This is where field protection declarations occur (`Protect`, `ProtectAll`, `AllowMutation`). Setup hooks cannot cancel the operation.

2. **Protection Snapshot Capture** -- After all setup hooks execute, the system serializes the current values of all protected properties into a dictionary keyed by property name. This snapshot is the baseline against which subsequent mutations are detected.

3. **Before** -- Pre-persistence hooks that may inspect and mutate the entity, access prior state, or cancel the operation by returning a `Cancel(reason, code)` result. Hooks execute in registration order; the first cancellation short-circuits remaining Before hooks.

4. **Protection Validation (pre-persist)** -- The system compares current protected property values against the snapshot captured in phase 2. Any discrepancy throws `InvalidOperationException` identifying the violated field. This catches mutations introduced by Before hooks.

5. **Persist** -- The framework invokes the caller-supplied persistence delegate with the (possibly mutated) current entity. On Upsert, the persisted entity (which may have database-generated values) replaces the context's current entity reference.

6. **After** -- Post-persistence hooks for side effects (cache eviction, event publishing, audit logging). After hooks cannot cancel the operation.

7. **Protection Validation (post-persist)** -- A second validation pass catches mutations introduced by After hooks.

For Load operations, phases 4 (Persist) is replaced by a no-op since the entity was already loaded, but all other phases execute identically.

### 3.2 Lazy Prior-State Loading

The `EntityEventPrior<TEntity>` class wraps a caller-supplied `Func<CancellationToken, ValueTask<TEntity?>>` loader delegate. Key behaviors:

- **Deferred execution.** The loader delegate is not invoked until a hook calls `context.Prior.Get(ct)`. If no hook in the pipeline accesses prior state, no database query is issued.

- **Single-load guarantee.** A `_loaded` boolean flag, protected by a lock, ensures the loader executes at most once per pipeline invocation. Subsequent calls to `Get()` return the cached value synchronously via `new ValueTask<TEntity?>(_value)`.

- **Empty sentinel.** For operations where prior state is semantically meaningless (e.g., Load), a static `Empty` instance wraps a delegate returning `null`, avoiding null-reference checks throughout the pipeline.

- **Per-entity scoping in batches.** In batch Upsert, each entity receives its own `EntityEventPrior<TEntity>` instance with a loader closure capturing that specific entity, ensuring prior-state isolation.

### 3.3 Field-Level Mutation Protection

The `EntityEventContext<TEntity>` class implements property-level immutability enforcement:

- **Reflection-based property getter map.** A `Lazy<IReadOnlyDictionary<string, Func<TEntity, object?>>>` builds compiled expression-tree delegates for every public readable instance property on `TEntity`. This map is computed once per closed generic type and cached.

- **`Protect(propertyName)`** -- Adds a named property to the protected set. Throws if the property does not exist on the entity type.

- **`ProtectAll()`** -- Adds every readable public property to the protected set.

- **`AllowMutation(propertyName)`** -- Removes a property from the protected set, enabling a "protect all, allow specific" pattern (e.g., `ProtectAll()` then `AllowMutation("UpdatedAt")`).

- **`CaptureProtectionSnapshot()`** -- Called internally after Setup phase completes. Iterates protected members, invokes compiled getters, stores `Dictionary<string, object?>` of current values.

- **`ValidateProtection()`** -- Called internally at two enforcement points (before persist, after After hooks). Iterates the snapshot dictionary, re-invokes getters on the current entity, and compares values using `Object.Equals`. Any mismatch throws with the offending field name.

### 3.4 Batch Disposition Tracking

Batch operations (`ExecuteUpsertMany`, `ExecuteRemoveMany`) maintain a list of `EntityOutcome` records, each capturing:

- The entity's key (`object?`)
- The lifecycle operation (`EntityEventOperation` enum)
- The `EntityEventResult` (proceed or cancel with reason/code)

The `EntityBatchResult` record classifies aggregate outcomes:

- **Success** -- All entities proceeded through the pipeline.
- **PartialSuccess** -- Some entities were cancelled by Before hooks; remaining entities were persisted.
- **Cancelled** -- The entire batch was aborted.

### 3.5 Atomic Batch Semantics

The `EntityEventOperationState` class exposes a `RequireAtomic()` method. When any hook calls this on any entity in a batch, the pipeline enforces all-or-nothing semantics: if any entity in the batch is cancelled by a Before hook and any entity has requested atomic execution, the entire batch throws `EntityEventBatchCancelledException` with the full outcomes list, preventing partial persistence.

### 3.6 Zero-Allocation Fast Path

Each `EntityEventRegistry<TEntity, TKey>` maintains boolean properties (`HasLoadPipeline`, `HasUpsertPipeline`, `HasRemovePipeline`) computed from the lengths of handler arrays. When no hooks are registered for a given operation on a given entity type, the executor's public methods bypass all context allocation, protection infrastructure, and phase execution, directly invoking the persistence delegate. This ensures zero overhead for entity types without lifecycle hooks.

### 3.7 Static Registration with Copy-on-Write Thread Safety

Handler arrays in `EntityEventRegistry<TEntity, TKey>` are managed with copy-on-write semantics under a lock:

```
lock (Gate)
{
    var copy = new THandler[target.Length + 1];
    Array.Copy(target, copy, target.Length);
    copy[target.Length] = handler;
    target = copy;
}
```

Reads during pipeline execution use `ReadOnlyMemory<T>` slices over the current array reference, which is safe against concurrent registration because the array reference is atomically replaced. The `ReadOnlyMemory<T>.Span` accessor provides bounds-checked, allocation-free iteration.

### 3.8 Fluent Registration API

The `EntityEventsBuilder<TEntity, TKey>` class provides a fluent chainable API:

```csharp
Article.Events
    .Setup(ctx => { ctx.ProtectAll(); ctx.AllowMutation("Slug"); })
    .BeforeUpsert(ctx => { /* normalize, validate, cancel */ })
    .AfterUpsert(ctx => { /* evict cache, publish event */ });
```

Both synchronous and asynchronous overloads are provided for each phase. Synchronous handlers are wrapped in `ValueTask`-returning delegates to avoid heap allocation on the synchronous path.

### 3.9 Test Reset Infrastructure

`EntityEventTestHooks.Reset<TEntity, TKey>()` clears all registered handlers for a given entity type, enabling test isolation without process restart. This replaces all handler arrays with empty arrays under the same lock used for registration.

---

## 4. Implementation Architecture

### 4.1 Type System

| Type | Role |
|------|------|
| `EntityEventExecutor<TEntity, TKey>` | Internal static orchestrator; contains `ExecuteLoad`, `ExecuteUpsert`, `ExecuteRemove` and batch variants |
| `EntityEventRegistry<TEntity, TKey>` | Internal static handler storage with copy-on-write arrays and pipeline-presence booleans |
| `EntityEventsBuilder<TEntity, TKey>` | Public fluent registration surface |
| `EntityEventContext<TEntity>` | Public mutable context flowing through phases; holds Current, Prior, Operation state, Items bag, protection infrastructure |
| `EntityEventPrior<TEntity>` | Public lazy-loading wrapper for prior entity state |
| `EntityEventResult` | Public readonly struct with `Proceed()` and `Cancel(reason, code)` factory methods |
| `EntityEventOperation` | Public enum: `Load`, `Upsert`, `Remove` |
| `EntityEventOperationState` | Public mutable state shared across hooks for a single entity; supports `RequireAtomic()` |
| `EntityOutcome` | Public sealed record: per-entity batch result with key, operation, and result |
| `EntityBatchResult` | Public sealed record: aggregate batch outcome with disposition enum |
| `EntityBatchDisposition` | Public enum: `Success`, `PartialSuccess`, `Cancelled` |
| `EntityEventCancelledException` | Public exception for single-entity cancellation |
| `EntityEventBatchCancelledException` | Public exception for batch cancellation with full outcomes list |
| `EntityEventTestHooks` | Public static test helper for registry reset |

### 4.2 Data Flow Diagram

```
Caller
  |
  v
ExecuteUpsert(entity, persistFn, priorLoaderFn, ct)
  |
  +-- [Fast path: no hooks registered] --> persistFn(entity, ct) --> return
  |
  +-- [Hooks registered]
       |
       v
     Allocate: EntityEventOperationState, EntityEventPrior<T>, EntityEventContext<T>
       |
       v
     Phase 1: RunSetup(context)
       |  - ctx.ProtectAll(), ctx.AllowMutation("X"), ctx.Protect("Y")
       |
       v
     Phase 2: CaptureProtectionSnapshot()
       |  - Snapshot protected field values via compiled expression-tree getters
       |
       v
     Phase 3: RunBefore(context, Upsert)
       |  - Hook may call ctx.Prior.Get(ct) --> triggers lazy DB load (once)
       |  - Hook may mutate ctx.Current (allowed fields only)
       |  - Hook may return Cancel(reason, code) --> throws EntityEventCancelledException
       |
       v
     Phase 4: ValidateProtection()
       |  - Compare current field values against snapshot
       |  - Throw on any protected field mutation
       |
       v
     Phase 5: persistFn(ctx.Current, ct) --> ctx.UpdateCurrent(persisted)
       |
       v
     Phase 6: RunAfter(context, Upsert)
       |  - Side effects: cache eviction, event publishing
       |
       v
     Phase 7: ValidateProtection()  (re-validate after After hooks)
       |
       v
     return ctx.Current
```

### 4.3 Batch Data Flow

```
ExecuteUpsertMany(entities[], persistFn, priorLoaderFn, ct)
  |
  +-- [Fast path: no hooks] --> persistFn(entities, ct)
  |
  +-- For each entity:
  |     |
  |     v
  |   Allocate per-entity: State, Prior (closure over entity), Context
  |     |
  |     v
  |   RunSetup --> CaptureProtectionSnapshot --> RunBefore
  |     |
  |     +-- [Cancelled] --> Record EntityOutcome, check IsAtomic
  |     |                    If atomic: break loop
  |     |                    Else: continue to next entity
  |     |
  |     +-- [Proceeded] --> ValidateProtection, add to payload list
  |
  v
  Check: if requiresAtomic AND payload.Count != entities.Count
    --> throw EntityEventBatchCancelledException(outcomes)
  |
  v
  persistFn(payload, ct)  -- only non-cancelled entities
  |
  v
  For each executing context: RunAfter --> ValidateProtection
  |
  v
  return persisted count
```

---

## 5. Novelty and Non-Obvious Aspects

The following aspects of this system, individually and in combination, constitute novel contributions to the state of the art in entity lifecycle management:

### 5.1 Lazy Prior-State with Single-Load Guarantee (Novel)

No existing ORM or entity framework provides a lazy-loading prior-state mechanism that (a) defers the database query until a hook explicitly requests it, (b) guarantees exactly-once loading across all hooks in the pipeline via thread-safe caching, and (c) provides a static `Empty` sentinel for operations where prior state is semantically inapplicable. EF Core's ChangeTracker eagerly loads original values; Rails/Django/Spring provide no prior state at all.

### 5.2 Setup-Phase Field Protection with Dual Enforcement (Novel)

The separation of a Setup phase (where protection rules are declared) from Before/After phases (where mutations occur) enables a declarative "protect all, allow specific" pattern. Protection is enforced at two boundaries -- after Before hooks and after After hooks -- catching violations from both pre-persist and post-persist logic. No existing system provides field-level mutation protection within lifecycle hooks.

### 5.3 Compiled Expression-Tree Property Getter Cache (Non-Obvious)

The protection mechanism builds `Expression.Lambda<Func<TEntity, object?>>` delegates for each property at first use, caches them in a `Lazy<IReadOnlyDictionary>` per closed generic type, and uses these compiled delegates for both snapshot capture and validation. This avoids reflection overhead on every lifecycle invocation while supporting arbitrary entity types without code generation or source generators.

### 5.4 Per-Entity Cancellation with Atomic Escalation in Batches (Novel)

The combination of per-entity `Cancel(reason, code)` results with `EntityEventOperationState.RequireAtomic()` creates a two-level batch control system: by default, cancelled entities are skipped and the remaining batch proceeds (PartialSuccess); but any hook can escalate to atomic semantics where a single cancellation aborts the entire batch. This gives domain logic fine-grained control over batch failure modes without requiring the caller to pre-classify batches.

### 5.5 Structured Cancellation with Machine-Readable Codes (Non-Obvious)

`EntityEventResult.Cancel(reason, code)` provides both human-readable explanation and machine-readable code, enabling upstream callers (e.g., REST controllers) to translate lifecycle cancellations into appropriate HTTP status codes and error responses without parsing exception messages.

### 5.6 Zero-Allocation Fast Path via Static Boolean Guards (Non-Obvious)

The `Has*Pipeline` properties, derived from handler array lengths, enable a branchless fast path that avoids allocating `EntityEventOperationState`, `EntityEventPrior<T>`, and `EntityEventContext<T>` when no hooks are registered. This is particularly significant in high-throughput systems where most entity types have no lifecycle hooks.

### 5.7 Copy-on-Write Registration with ReadOnlyMemory Iteration (Non-Obvious)

Handler registration uses lock-protected array replacement (copy-on-write), while pipeline execution reads through `ReadOnlyMemory<T>.Span` without locking. This provides safe concurrent registration during application startup without penalizing the hot execution path with synchronization overhead.

### 5.8 Combined System (Novel)

The combination of lazy prior-state, field-level protection with dual enforcement, per-entity batch cancellation with atomic escalation, and zero-allocation fast paths in a single coherent pipeline represents a novel architecture not found in any existing ORM, entity framework, or persistence library.

---

## 6. Prior Art Comparison

| Capability | Rails ActiveRecord | Django Signals | EF Core SaveChanges | Spring JPA | Koan Framework (this disclosure) |
|---|---|---|---|---|---|
| Lifecycle hooks | `before_save`, `after_save`, etc. | `pre_save`, `post_save` signals | `SavingChanges`, `SavedChanges` events | `@PrePersist`, `@PostPersist` annotations | Setup, Before{Load,Upsert,Remove}, After{Load,Upsert,Remove} |
| Prior entity state | Not provided | Not provided | ChangeTracker (eager, always loaded) | Not provided | Lazy-loaded via `EntityEventPrior<T>`, only fetched on `Get()` |
| Field-level mutation protection | Not provided | Not provided | Not provided | Not provided | `Protect()`, `ProtectAll()`, `AllowMutation()` with dual-point enforcement |
| Hook cancellation | `throw :abort` (exception-based) | Not provided (return value ignored) | Not directly supported | Not provided | `EntityEventResult.Cancel(reason, code)` with structured codes |
| Batch disposition tracking | Not provided | Not provided | Not provided | Not provided | `EntityBatchResult` with `Success`/`PartialSuccess`/`Cancelled` |
| Atomic batch escalation | Not applicable | Not applicable | Not applicable | Not applicable | `RequireAtomic()` on per-entity state, enforced at batch level |
| Zero-overhead fast path | No (callbacks always evaluated) | No (signal dispatch always runs) | No (event infrastructure always present) | No (annotation scanning always occurs) | Yes (`Has*Pipeline` boolean guards skip all allocation) |
| Per-entity scoping in batches | Single entity callbacks only | Single entity signals only | Bulk operations bypass interceptors | Bulk operations bypass listeners | Per-entity context, prior, and state within batch loop |
| Test isolation | Global callback reset | Signal disconnect | Event handler removal | No built-in mechanism | `EntityEventTestHooks.Reset<T,K>()` clears all handlers |

### Key Differentiators from Closest Prior Art (EF Core)

Entity Framework Core's `ChangeTracker` provides the closest existing mechanism for prior-state access, but differs fundamentally:

1. **EF Core eagerly loads original values** for all tracked entities into `OriginalValues` property bags, regardless of whether any interceptor inspects them. The disclosed system only loads prior state when a hook calls `Get()`.

2. **EF Core provides no field-level protection.** Any interceptor or `SaveChanges` override can mutate any property. The disclosed system enables declarative field protection with compile-time property name validation and runtime enforcement.

3. **EF Core's `SavingChanges` event operates on the entire `DbContext`**, not per-entity. Cancellation cancels all pending changes. The disclosed system provides per-entity cancellation within batches with configurable partial-success semantics.

4. **EF Core has no batch disposition concept.** Bulk operations either succeed entirely or fail entirely (via transaction rollback). The disclosed system tracks per-entity outcomes and supports PartialSuccess.

---

## 7. Antagonist Analysis

This section anticipates challenges to the novelty and non-obviousness of the disclosed system and provides counterarguments.

### Challenge 1: "Lazy loading is a well-known pattern; applying it to prior state is obvious."

**Response.** Lazy loading as a general software pattern is indeed well established. The novelty lies not in the lazy loading pattern itself but in its specific application within an entity lifecycle pipeline context, combined with: (a) the per-entity isolation in batch operations via closure capture, (b) the thread-safe single-load guarantee with lock-protected `_loaded` flag and `ValueTask` fast path for subsequent access, (c) the `Empty` sentinel pattern that avoids null checks throughout the pipeline for operations where prior state is semantically inapplicable, and (d) the integration with field-level protection where the prior state and current state can both be compared against protection snapshots. No existing ORM has combined these elements despite lazy loading being available as a technique for over two decades.

### Challenge 2: "Field protection could be achieved with read-only DTOs or immutable records."

**Response.** Read-only DTOs and immutable records enforce immutability at the type level, which is a different concern. The disclosed system solves the problem of *selective, contextual, runtime-configurable* field immutability within a mutable pipeline. The entity must remain mutable (hooks need to modify allowed fields like `UpdatedAt` or `Slug`), but specific fields must be protected from accidental or malicious mutation by downstream hooks. Immutable types cannot provide this selective protection. Additionally, the "protect all, allow specific" pattern (`ProtectAll()` + `AllowMutation("Slug")`) provides a safe default that protects against newly added properties automatically -- a capability fundamentally unavailable with compile-time immutability.

### Challenge 3: "The combination of individually known patterns does not create novelty."

**Response.** The specific combination and integration architecture is novel and non-obvious. Consider the interaction between lazy prior-state and field protection: a Before hook can call `ctx.Prior.Get()` to compare old and new values, then decide whether to cancel -- but the protection system ensures that the comparison itself does not cause mutations that would violate protection. The dual enforcement points (before and after persistence) are not a simple addition but a design choice that catches distinct failure modes: Before enforcement catches hook bugs; After enforcement catches persistence-layer side effects. The atomic escalation mechanism in batches interacts with per-entity cancellation to create a two-level control system that no existing framework provides. These interactions produce emergent behavior that is greater than the sum of individual patterns.

### Challenge 4: "Static generic classes with copy-on-write arrays are a standard implementation technique."

**Response.** The implementation technique is not claimed as novel in isolation. The novelty lies in how the static generic architecture enables the zero-allocation fast path: because `EntityEventRegistry<TEntity, TKey>` is a distinct static class per closed generic type, the `Has*Pipeline` boolean can be evaluated without any allocation, dictionary lookup, or virtual dispatch. This is a performance-critical design decision that emerges from the choice to use static generics rather than instance-based registries, and it directly addresses the real-world performance problem of hook overhead on unregistered entity types.

### Challenge 5: "Event sourcing systems already track prior state and mutations."

**Response.** Event sourcing systems track state transitions as a sequence of domain events and can reconstruct any prior state by replaying events. This is a fundamentally different architecture: (a) event sourcing requires the entire persistence model to be event-based, while the disclosed system works with any persistence mechanism (SQL, NoSQL, file, API); (b) event sourcing prior state is reconstructed from the event log, while the disclosed system loads it directly from the current persistence store via a caller-supplied delegate; (c) event sourcing does not provide field-level mutation protection within command handlers; (d) event sourcing does not provide the lifecycle pipeline phases (Setup, Before, After) with the specific ordering and enforcement guarantees described here.

### Challenge 6: "Middleware pipelines (ASP.NET Core, Express.js) already provide ordered hook execution with cancellation."

**Response.** HTTP middleware pipelines operate on request/response pairs and provide different semantics: (a) middleware does not have a concept of "the entity being persisted" and cannot provide field-level protection on domain objects; (b) middleware cancellation aborts the entire request, not individual entities within a batch; (c) middleware does not provide lazy prior-state loading; (d) middleware does not distinguish Setup, Before, and After phases with different return types (`ValueTask` for Setup/After, `ValueTask<EntityEventResult>` for Before). The disclosed system borrows the general concept of an ordered pipeline but applies it to a fundamentally different domain (entity persistence) with domain-specific capabilities (prior state, protection, batch disposition) that have no middleware analogue.

---

## Appendix A: Source File Inventory

All source files reside in `src/Koan.Data.Core/Events/` within the Koan Framework repository:

| File | Lines | Purpose |
|------|-------|---------|
| `EntityEventExecutor.cs` | 343 | Pipeline orchestration for Load, Upsert, Remove (single and batch) |
| `EntityEventRegistry.cs` | 87 | Static handler storage with copy-on-write thread safety |
| `EntityEventsBuilder.cs` | 118 | Fluent registration API with sync/async overloads |
| `EntityEventContext.cs` | 189 | Runtime context with field protection infrastructure |
| `EntityEventPrior.cs` | 56 | Lazy prior-state loader with single-load guarantee |
| `EntityEventResult.cs` | 43 | Readonly struct for proceed/cancel outcomes |
| `EntityEventOperation.cs` | 13 | Operation enum (Load, Upsert, Remove) |
| `EntityEventOperationState.cs` | 19 | Per-operation mutable state with atomic escalation |
| `EntityOutcome.cs` | 11 | Per-entity batch outcome record |
| `EntityBatchResult.cs` | 18 | Aggregate batch result with disposition |
| `EntityBatchDisposition.cs` | 11 | Disposition enum (Success, PartialSuccess, Cancelled) |
| `EntityEventCancelledException.cs` | 20 | Single-entity cancellation exception |
| `EntityEventBatchCancelledException.cs` | 21 | Batch cancellation exception with outcomes |
| `EntityEventTestHooks.cs` | 12 | Test isolation helper |

## Appendix B: Usage Example

```csharp
// Entity type with standard Koan entity-first pattern
public class Article : Entity<Article, Guid>
{
    public string Title { get; set; }
    public string Slug { get; set; }
    public Guid TenantId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

// Lifecycle registration (typically in a static constructor or module initializer)
Article.Events
    .Setup(static ctx =>
    {
        // Protect all fields by default, then allow controlled mutations
        ctx.ProtectAll();
        ctx.AllowMutation(nameof(Article.Slug));
        ctx.AllowMutation(nameof(Article.UpdatedAt));
    })
    .BeforeUpsert(static async ctx =>
    {
        // Lazy prior-state: only queries DB if this line executes
        var prior = await ctx.Prior.Get(ctx.CancellationToken);

        // Normalize slug on every save
        ctx.Current.Slug = SlugHelper.Generate(ctx.Current.Title);
        ctx.Current.UpdatedAt = DateTime.UtcNow;

        // Prevent title from being blanked on update
        if (prior is not null && string.IsNullOrWhiteSpace(ctx.Current.Title))
            return ctx.Cancel("Title cannot be cleared on existing articles.", "ARTICLE_TITLE_REQUIRED");

        return ctx.Proceed();
    })
    .AfterUpsert(static ctx =>
    {
        ArticleCache.Evict(ctx.Current.Id);
        // If a hook accidentally mutated TenantId here, ValidateProtection()
        // would throw after this phase completes.
    });
```

In this example, if any Before or After hook mutates `TenantId`, `CreatedAt`, `Title`, or `Id`, the pipeline throws `InvalidOperationException` identifying the violated field. Only `Slug` and `UpdatedAt` may be changed. The prior entity state is loaded from the database only if the `BeforeUpsert` hook executes (which it does here), and only once regardless of how many hooks call `ctx.Prior.Get()`.

---

**End of Defensive Publication**

*This document is published as prior art to prevent patenting of the described techniques. The described system is implemented in the Koan Framework, an open-source .NET framework. This publication is intended to be available as prior art effective as of the date of disclosure.*
