# JOBS-0003: Per-type job entities (table-per-type), generic runtime, typed payloads

**Status**: Accepted
**Date**: 2026-05-28
**Deciders**: Enterprise Architect
**Scope**: Koan.Jobs.Core - the Job model, store, runtime, builder
**Related**: JOBS-0001 (Jobs pillar, superseded in part), JOBS-0002 (lanes/coalesce/Push/delayed-visibility, re-homed), ADR-0017 (WaitFor)

---

## Context

JOBS-0001 modeled every job as a subtype of one abstract `Job : Entity<Job>`, stored in a single
`Job` set. Persisting that set requires the store to deserialize a heterogeneous collection back
into concrete types, which needs a per-row type discriminator. The Mongo connector disables
discriminators globally for performance (`NoDiscriminatorConvention` on `object`), so the abstract
`Job` cannot be rehydrated: `Entity<Job>.Get` throws `Cannot create an instance of … Job because it
is an abstract class`. Consumers worked around this by keeping persisted jobs out of the abstract
set entirely (the app's `OperationsRun`, a hand-rolled concrete row with a `Kind` field doing the
same single-table-inheritance the framework couldn't).

Reaching for a discriminator subsystem treats the symptom. The cleaner cut is to remove the
heterogeneous set: model each concrete job as its own homogeneous `Entity<T>` set. There is then
nothing to disambiguate, no discriminator, no abstract instantiation, and persistence works
identically on every provider. The job types only ever shared *runtime mechanics* (status, retry,
lanes, progress, dependencies), not *domain data*; that shared substrate belongs in a generic base,
not a shared collection.

## Decision

Rebuild the Jobs model as **table-per-type**. The `Job<…>`-as-single-set model from JOBS-0001 is
superseded; the JOBS-0002 capabilities (lanes, coalesce, `Push`, delayed-visibility queue) re-home
onto the new runtime unchanged in spirit.

### 1. `Job<T> : Entity<T>` (CRTP), one homogeneous set per concrete type

```csharp
public abstract class Job<T> : Entity<T>, IKoanJob<T>
    where T : Job<T>, IKoanJob<T>, new()
{
    public JobStatus Status { get; set; } = JobStatus.Created;
    public double Progress { get; set; }
    public string? ProgressMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? QueuedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public TimeSpan? Duration { get; set; }
    public string? CorrelationId { get; set; }
    public string? LastError { get; set; }
    public string? ResolvedLane { get; set; }      // stamped at enqueue (JOBS-0002)
    public string? CoalesceKey { get; set; }        // stamped at enqueue (JOBS-0002)
    public List<JobRef> WaitFor { get; set; } = new();
    public Dictionary<string, object?> Metadata { get; set; } = new();

    protected abstract Task Do(IJobProgress progress, CancellationToken ct);
}
```

There is **no** non-generic `Job : Entity<Job>` set. `CrawlJob : Job<CrawlJob>` is its own
`Entity<CrawlJob>` collection. The shared runtime substrate lives on the generic base (code reuse +
uniform shape), not in a shared store. No discriminator, no `_t`, provider-agnostic.

### 2. Typed payloads (no JSON blobs)

Concrete jobs carry their context and result as **native typed properties**, because the collection
is homogeneous:

```csharp
public sealed class CrawlJob : Job<CrawlJob>
{
    public CrawlContext Context { get; set; } = default!;
    public CrawlResult?  Result  { get; set; }
    protected override Task Do(IJobProgress progress, CancellationToken ct) { /* reads this.Context, sets this.Result */ }
}
```

`ContextJson` / `ResultJson` are removed. Payloads become **queryable** (e.g.
`CrawlJob.Query(j => j.Context.ProviderId == x && j.Status == Running)`), which strengthens dedup and
admin filtering that opaque blobs made impossible.

### 3. `IKoanJob<T>` contract + per-type policy

`IKoanJob<T>` is the contract the generic runtime binds to. Policy (lane, retry, coalesce-key
derivation) is expressed as **instance virtual members** on `Job<T>` with sensible defaults, so a
job overrides only what it needs:

```csharp
protected virtual string Lane => JobLanes.Default;          // "default"
protected virtual RetryPolicy Retry => RetryPolicy.None;
protected virtual string? CoalesceKey() => null;            // derive from this.Context
```

This replaces the `[JobLane]` / `[RetryPolicy]` attributes from JOBS-0002 with co-located,
refactor-safe, reflection-free policy. (Static-abstract interface members were considered for
no-instance access; declined: CRTP + static-abstract is finicky, and the runtime always has an
instance at enqueue, which is where lane/coalesce are read.)

### 4. Generic runtime; the store layer collapses

The dispatch queue stays in-memory (the time-aware `InMemoryJobQueue` from JOBS-0002). A
`JobQueueItem` carries the concrete `Type` + id + resolved lane. The worker (single claimer +
concurrent dispatch + graceful drain) resolves a generic `JobRunner<T>` by reflection (existing
pattern), rehydrates the typed job via `Entity<T>`, and runs `Do`.

`IJobStore` / `InMemoryJobStore` / `EntityJobStore` / `JobStoreResolver` / `JobStorageMode` are
**removed**. Jobs are persisted via plain `Entity<T>` (`Save`/`Get`/`Query`/`Page`). "In-memory vs
persisted" becomes ordinary Koan provider routing: **jobs persist by default** (to the app's default
provider); a job type that should be transient routes its set to the in-memory data connector. The
per-call `.Persist()` is gone (persistence is a property of the type, not the call site).

- **Coalesce** (JOBS-0002): `T.Query(j => j.CoalesceKey == key && j.Status is not terminal)`.
- **Recovery on boot**: fan-out over the job-type registry, re-enqueue each type's non-terminal rows.
- **Archival/retention**: per-type sweep over each set.
- **What is running now**: read live lane/worker state (telemetry), not a persisted union.

### 5. `JobRef<T>` typed handle

A job reference is `(Type, id)`, surfaced as `JobRef` (non-generic, stored) and `JobRef<T>` (typed
API). It serves WaitFor, cancel, and drill-in. This removes the bare-id ambiguity that per-type sets
otherwise introduce (a bare id no longer implies a collection). WaitFor becomes typed:
`WaitFor(JobRef<CrawlJob>.For(id))` for a specific job, `WaitFor<CrawlJob>()` for "any completed
CrawlJob exists" (ADR-0017 semantics preserved, now per-type).

### 6. No unified cross-kind table

Each domain owns its job set and its queue concern; the runtime substrate stays unified in the
framework. Cross-cutting operations are registry-driven fan-out (recovery, archival) or runtime
telemetry (what is running). A unified, paginated, cross-kind persisted view is **not** built:
assembling it would require fan-out + client-side merge (breaking server-side pagination/totals),
and per-kind views serve the need. If ever required, it is a non-invasive later add (a read-model
projection over the unchanged per-type write models).

## Consequences

### Positive
- No discriminator, no abstract-instantiation failure, no `_t`: persistence works on every provider.
- Typed, queryable payloads replace opaque JSON blobs.
- The bespoke two-mode store collapses into `Entity<T>` + provider routing: fewer, more meaningful parts.
- Policy is co-located and compiler-checked; no attribute reflection.
- Coalesce/recovery/archival become plain typed queries / registry fan-out.
- Consumers' single-table-inheritance workarounds (the app's `OperationsRun`) can be deleted.

### Negative / watch
- Cross-kind operations fan out over a job-type registry (N small; deterministic). No single-query union.
- Bare cross-kind job-id references need a typed `JobRef`; cross-*domain* id dependencies are rare.
- This is a breaking rebuild of the pillar and its consumers (accepted: greenfield, break-and-rebuild).
- Per-type sets duplicate the shared runtime columns across collections (inherent to table-per-type; fine).

## Migration

- Koan.Jobs.Core rebuilt per the above; tests reshaped per type.
- Downstream (Downstream consumer, ADR-0020 Track B) collapses to deletion: jobs persist by default, the
  `OperationsRun` apparatus is removed, the admin matrix repoints to per-kind `T.Query`. This
  **supersedes ADR-0009 rev-2** in the app: the discriminator workaround is no longer needed because
  the framework no longer persists an abstract set.

## References
- JOBS-0001 (Jobs pillar), JOBS-0002 (lanes/coalesce/Push/delayed-visibility), ADR-0017 (WaitFor)
- `Entity<TEntity>` CRTP base (`Koan.Data.Core.Model`)
- Mongo `NoDiscriminatorConvention` (`Koan.Data.Connector.Mongo`) - the constraint this design removes the dependency on
