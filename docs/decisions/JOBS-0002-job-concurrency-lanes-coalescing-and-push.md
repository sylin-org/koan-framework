# JOBS-0002: Job Concurrency Lanes, Coalescing, and Push

**Status**: Accepted
**Date**: 2026-05-28
**Deciders**: Enterprise Architect
**Scope**: Koan.Jobs.Core — execution worker, builder surface, options
**Related**: JOBS-0001 (Jobs pillar), ADR-0017 (cross-job WaitFor)

---

## Context

The Jobs pillar (JOBS-0001) already delivers entity-first, provider-transparent, durable
(`.Persist()`), observable jobs with retry, host rate-gating, `WaitFor` dependencies, progress,
cancellation, and archival. Three gaps surface when the pillar runs real background load,
especially on resource-constrained single-host deployments:

1. **Serial worker / head-of-line blocking.** `JobWorkerService` drains the queue
   (`IJobQueue` = `Channel<JobQueueItem>`) and `await _executor.Execute(item)` **one item at a
   time**. A single slow job (a multi-second download or image transform) blocks every queued job
   behind it. There is no way to run independent work concurrently, and — conversely — no bound
   that protects a constrained box from N heavy jobs running at once.

2. **No resource-bounded concurrency.** The only concurrency control today is the per-host *rate*
   gate (`IHostRateGate`: requests-over-time against an upstream). There is no *concurrency* limit
   per resource class — e.g. "at most 10 concurrent outbound connections" or "at most 3 concurrent
   CPU-bound image transforms." These are orthogonal: a rate gate paces calls to one host; a lane
   caps simultaneous permits across a resource.

3. **No coalescing.** Each `Start(...)` mints a new job id. Fan-out workloads (re-derive every row
   after a rule change; re-evaluate a work-cluster on each upsert) enqueue thousands of redundant
   jobs for the same logical unit, with no way to collapse duplicates.

These are general framework concerns, not application-specific, so they belong in the pillar.

## Decision

Extend `Koan.Jobs.Core` with four capabilities (three additive + one queue redesign). The
`Job<T,TContext,TResult>` contract, `Execute`, `.Persist()`, `.Run()`, and `.Wait()` are unchanged.

### 1. Concurrency lanes

A **lane** is a named, config-tunable concurrency limit that the worker honours when dispatching
job execution. Lanes both add resource bounds and remove head-of-line blocking (independent lanes
run concurrently).

- **Declaration** — a job is assigned to a lane by, in precedence order: a builder call
  `Start(ctx).Lane("cpu-transform")`, a `[JobLane("cpu-transform")]` attribute on the job type, or
  the default lane (`"default"`) when neither is present.
- **Registry** — `JobLaneRegistry` holds one bounded async gate (**`SemaphoreSlim`** per lane —
  dependency-free; `ConcurrencyLimiter` only if richer queue metrics are wanted later), with caps
  from config (`Koan:Jobs:Lanes:{name}`, default `JobsOptions.DefaultLaneConcurrency` =
  `Environment.ProcessorCount`). Lane name resolves builder > `[JobLane]` > `"default"` and is
  stamped on the `JobQueueItem` at enqueue (so re-enqueues preserve it).
- **Worker model** — the worker keeps a **single serial claimer** reading the channel (no
  claim contention) and dispatches each claimed item to a tracked task. The lane permit wraps
  **only actual execution** (around the job-body runner), not the pre-checks or deferrals — see
  the delayed-visibility decision below, which guarantees a deferring job releases its permit
  immediately rather than holding it through a backoff sleep. Independent lanes run in parallel;
  each lane is capped; a busy lane never blocks another lane. Graceful drain awaits in-flight tasks
  on shutdown.

### 2. Coalesce-by-key

`Start(ctx).Coalesce(key)` makes enqueue idempotent for the pair `(jobType, key)`: if a
**non-terminal** job of the same type with the same coalesce key already exists, the builder
returns that job instead of creating a new one. Backed by the job store
(`IJobStore.FindCoalescable(typeName, key, ...)`); InMemory uses an index dictionary, Entity uses a
query over a stored `CoalesceKey` + non-terminal `Status`. Coalescing is opt-in; absent a key,
every `Start` is a distinct job (unchanged behaviour).

### 3. Push (terse durable enqueue)

`MyJob.Push(context)` is sugar for `Start(context).Persist().Run()` — the durable
fire-and-forget submit, returning the job handle. It exists to make the common "submit work and
forget" path a single obvious verb consistent with the pillar's semantic-ergonomics tenet
(JOBS-0001 #3). It adds no capability beyond the builder chain it expands to. Lane/coalesce remain
available via the builder (`Start(ctx).Persist().Lane(...).Coalesce(...).Run()`) when the common
form isn't enough.

### 4. Delayed-visibility re-enqueue (queue redesign)

Today the executor handles retry / rate-gate / dependency-block by `await Task.Delay(backoff)`
**inline inside `Execute`** then re-enqueuing — which, under lanes, would hold the lane permit
through the sleep (a `cpu-transform` lane of failing/backing-off jobs, or an `egress` lane of
rate-gated jobs, would starve). Redesign the queue around **visible-at**:

- `IJobQueue.Enqueue(item, DateTimeOffset visibleAt)`; the in-memory queue becomes time-aware (a
  due-time structure feeding the ready `Channel`). The Entity store already persists `QueuedAt`.
- The executor's deferral paths set the re-enqueue's `visibleAt = now + delay` and **return
  immediately** (no inline `Task.Delay`), so the lane permit / worker slot is released at once.
- The lane permit wraps only the job-body run, never a sleeping deferral.

**Bonus:** this gives scheduled / delayed jobs for free (`Start(ctx).After(delay)` / run-at-T),
which the pillar lacks today.

### Configuration

```jsonc
"Koan": { "Jobs": { "Lanes": {
  "default":       { "MaxConcurrency": 4 },
  "cpu-transform": { "MaxConcurrency": 3 },
  "egress":        { "MaxConcurrency": 10 }
}}}
```

`JobsOptions` gains a `Lanes` dictionary (name → cap) with a `DefaultLaneConcurrency` fallback.

## Consequences

### Positive
- Removes head-of-line blocking; constrained hosts stay responsive under heavy background load.
- Resource-class concurrency caps as one config-tunable primitive, orthogonal to and composable
  with the existing host rate gate.
- Fan-out workloads collapse to one job per logical unit via `Coalesce`.
- `Push` gives the pillar a one-verb durable-submit surface; everything stays opt-in and backward
  compatible.
- Delayed-visibility deferral means retry/gate/block backoffs no longer occupy lane permits, and
  scheduled/delayed jobs (`VisibleAt`) come for free.

### Negative / watch
- The worker gains real concurrency; lane permit acquire/release and in-flight task tracking must
  be correct (graceful drain on shutdown). Kept tractable by the single-serial-claimer model.
- Coalescing adds a store lookup on enqueue and a `CoalesceKey` index; negligible at expected
  volumes, but a new store method to implement per adapter.
- Lane mis-tuning (cap too low) can starve a lane; surfaced via existing job telemetry (queue
  depth / in-flight per lane).

## References
- JOBS-0001 — Jobs pillar (entity-first task management)
- ADR-0017 — cross-job WaitFor primitive
- `System.Threading.RateLimiting.ConcurrencyLimiter`, `System.Threading.Channels`
