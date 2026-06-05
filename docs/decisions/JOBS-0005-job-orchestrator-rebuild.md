# JOBS-0005: Job Orchestrator rebuild — a durable, edge+level-triggered state machine

**Status**: **Accepted (2026-06-04)** · **Implemented — in-memory + durable tiers, SQLite-verified (2026-06-04; see §16)**. All positions ratified by the architect (§12). Greenfield rebuild: **supersedes JOBS-0001, JOBS-0002, JOBS-0003** and the in-code "JOBS-0004" partition tier. The current `Koan.Jobs.Core` module is discarded.
**Date**: 2026-06-04
**Deciders**: Enterprise Architect
**Scope**: Replace the scattered job subsystem with a single, encapsulated **Job Orchestrator** concern that owns a ledger, dispatch/recall, cancellation, and scheduling. Define the entity-first authoring surface, the capability-graded backend, and the state-machine execution model (edge-triggered `Submit` + level-triggered `Schedule`).
**Related**: supersedes JOBS-0001/0002/0003 · DATA-0077 (partitions / `EntityContext.With`) · Entity Transfer `Entity<T>.Move()` · DATA-0098 (boundary encoding) · ARCH-0084 (capability tokens + provider election) · ARCH-0086 (`KoanModule` bootstrap + `Report`) · `[KoanDiscoverable]` + `KoanRegistry.GetDiscoveredImplementors` (auto-registration) · `Koan.Messaging.IMessageBus` (distributed transport) · ARCH-0079 (integration tests as canon) · the Koan redesign initiative ("fewer but more meaningful parts").

---

## 1. Context

A domain audit (this branch) found the current jobs subsystem has **no encapsulated orchestrator**. "The orchestrator" is emergent from ~10 collaborating types + **4 background services** (`JobWorkerService`, `JobRecoveryService`, `JobOrphanReaper`, `JobArchivalService`) + ambient statics (`JobRuntime`, `JobEnvironment` → `AppHost.Current`) + reflection fan-out (`JobExecutor`, `JobTypeRegistry`/`JobTypeOps<T>` via `MakeGenericType`). Concretely it fails the three things an orchestrator must do:

- **No ledger.** Job state is a single mutable `Job<T>` row written in place from ≥5 sites; history is overwritten; events are fire-and-forget.
- **No real cancellation of stuck jobs.** `JobCancellations` is in-memory, single-process, lost on restart; a stuck job is *reaped (retried)*, never cancelled.
- **Scattered dispatch/recall** over an in-memory queue; durability is implicit (the rows are the truth, reconstructed by recovery/reaper after restart).

It is also **un-testable in isolation** (requires booting `AppHost` + a live store) and couples data with behavior (`Job<T> : Entity<T>` *and* abstract `Do`), which forces the reflection/CRTP machinery and table-per-type sets (so "all Running jobs" cannot be queried).

Because the framework is greenfield (< 1.0), this is the moment for a break-and-rebuild that gets the architecture lean and meaningful.

## 2. Forces / principles

1. **Reference = Intent.** Implementing the interface auto-wires the job; no manual registration.
2. **Entity-first authoring.** A job is a normal `Entity<T>`; behavior is co-located with the type. The *user* never sees the orchestrator.
3. **One concern owns the lifecycle.** A single `JobOrchestrator` owns ledger + dispatch/recall + cancellation + scheduling. No diffuse mutation.
4. **The durable store is the truth; the queue is derived.** Eliminate the volatile queue that must be reconciled.
5. **Constant delivery contract across tiers.** At-least-once + idempotent handlers — *everywhere*. Adding infrastructure changes durability/scale, never correctness.
6. **Fewer, more meaningful parts.** Scheduling subsumes reaping + recovery + recurring; physical layout hides behind one interface.
7. **Hold the line.** Linear state progression only. DAGs, parallel fan-out, and compensation are a future, separate workflow pillar — not this.

## 3. Vocabulary (decided)

Separate the entity from the unit of work — the single most clarifying rename:

| Term | Meaning |
|---|---|
| **Work-item** | the `Entity<T>` that *defines* background actions (e.g. `PresetPackage`). It is **not** "a job." |
| **`IKoanJob<T>`** | the marker the entity implements: *"this entity defines jobs."* Carries the static `Execute` handler. |
| **Action** (a.k.a. **Stage**) | a typed token (`Stage.Fetch`) naming **both** a unit of work **and** the resting state an entity sits in. Action names *are* states. |
| **Job** | the **enqueued unit** = (work-item id × action × lifecycle). This is the **ledger entry**, not the entity. |
| **`IJobLedger`** | the single source of truth: the durable record of every Job + its transitions. Also *is* the queue. |
| **`JobOrchestrator`** | the one concern that claims, executes, settles, recalls, cancels, and schedules. |
| **`IJobTransport`** | how dispatch reaches a worker (in-proc / message bus). Optional; selected by capability. |

## 4. Decision — the authoring surface

### 4.1 The entity + static handler

```csharp
public interface IKoanJob<TSelf> where TSelf : Entity<TSelf>, IKoanJob<TSelf>
{
    // ONE method. Single-action jobs ignore ctx.Action; multi-action jobs switch on it.
    static abstract Task Execute(TSelf job, JobContext ctx, CancellationToken ct);
}
```

- `static abstract` keeps the **record pure data** while co-locating behavior with the type (no separate handler class, no instance-on-data coupling). The orchestrator captures each discovered type's static into a `Func<JobRecord, JobContext, CancellationToken, Task>` **once at bootstrap** — **no per-dispatch reflection**.
- **DI in handlers**: `ctx.Services` for the common case; an optional registered `IKoanJobHandler<T>` class is the escape hatch for DI-heavy bodies (Open Q §12.10).

### 4.2 `JobContext`

```csharp
public sealed class JobContext
{
    public string Action { get; }                // the stage being executed ("" for single-action)
    public string JobId { get; }
    public IServiceProvider Services { get; }    // DI for handler bodies
    public ILogger Logger { get; }
    public CancellationToken Cancellation { get; }

    public JobState State { get; }               // READ-ONLY orchestration snapshot — for STATEFUL decisions

    public Task Progress(double fraction, string? message = null);   // DURABLE progress -> ledger
    public void ContinueWith(string action);     // branch: override the next stage (replaces [JobChain] default)
    public void StopChain();                     // terminal after this step, despite [JobChain]
    public void Reschedule(TimeSpan after);       // defer THIS job (same stage) by a relative delay; NO attempt consumed
    public void Reschedule(DateTimeOffset until); // defer to an ABSOLUTE time (e.g. "tomorrow 09:00")
    public void Backoff(TimeSpan after, string? key = null); // set a shared resource gate (default = the job's gate key) + reschedule
}

// Read-only view of THIS job's ledger entry, so a handler can make STATEFUL decisions
// (e.g. "already rescheduled 3x -> defer until tomorrow instead of +5m"). The work-item passed to
// Execute is the MUTABLE saga state; JobState is the IMMUTABLE orchestration record (work-item != Job, §3).
public sealed record JobState(
    JobStatus Status,
    string Action,                 // current stage
    int Attempt,                   // failed-and-retried count
    int Reschedules,               // cooperative-deferral count (SEPARATE from Attempt — Reschedule != Retry)
    DateTimeOffset FirstSubmittedAt,
    DateTimeOffset? LastSettledAt,
    string? LastError,
    string? DeferReason,
    DateTimeOffset? Deadline,
    string? CorrelationId);
```

### 4.3 Submit ergonomics (edge-triggered)

Job operations live under a `.Job` (instance) / `.Jobs` (static) accessor so they never pollute the model's domain surface or the data verbs (`Save`/`Get`/`Query`/`Delete`). `model.Job` = "this instance's job ops"; `MyModel.Jobs` = "the job subsystem for this type" (by-id, ledger queries, batch). See Open Q §12.14 for the accessor mechanics and the static-vs-instance constraint.

```csharp
// single — instance accessor
await new ThumbnailJob { SourceUrl = url }.Job.Submit();          // single-action: no action arg
await pkg.Job.Submit(Stage.Fetch);                               // multi-action
await pkg.Job.Submit(Stage.Fetch, after: TimeSpan.FromMinutes(5)); // delayed

// batch — 1000 instances, one bulk enqueue (constrained IEnumerable<T> extension; collision-free)
await packages.Submit(Stage.Fetch);

// type-level facade — by-id, ledger queries
await PresetPackage.Jobs.Status(id);
var running = await PresetPackage.Jobs.Where(j => j.Status == JobStatus.Running);
```

### 4.4 Cancellation (durable, cross-process)

```csharp
await pkg.Job.Cancel();                 // durable marker on the ledger entry (this instance)
await PresetPackage.Jobs.Cancel(id);    // by id, from any node/process
```

### 4.5 Awaiting a result

```csharp
var handle = await report.Job.Submit(Stage.Generate);
JobOutcome outcome = await handle.Completion(timeout: TimeSpan.FromMinutes(2)); // Completed | Failed | Cancelled | Timeout
```

## 5. Decision — declarative policy (attributes)

```csharp
public static class Stage   // typed tokens; PERSISTED BY NAME in the ledger (never ordinals)
{
    public const string PrepareToFetch = nameof(PrepareToFetch);
    public const string Fetch   = nameof(Fetch);
    public const string Parse   = nameof(Parse);
    public const string Mint    = nameof(Mint);
    public const string Publish = nameof(Publish);
}

[JobChain(Stage.Fetch, Stage.Parse, Stage.Mint, Stage.Publish)]       // type: linear auto-advance on success
[JobAction(Stage.PrepareToFetch, Schedule = "00:10:00")]              // LEVEL-triggered reconcile sweep, every 10m
[JobAction(Stage.Fetch,   Timeout = "00:10:00", MaxAttempts = 5, OnFailure = OnFailure.Abort, Lane = "upstream", MaxConcurrency = 4)]
[JobAction(Stage.Publish, Timeout = "00:00:30", MaxAttempts = 1)]
[JobIdempotent(nameof(Source), nameof(Version))]                      // dedupe re-delivery + coalesce concurrent dups
public sealed class PresetPackage : Entity<PresetPackage>, IKoanJob<PresetPackage> { … }
```

- **`[JobAction(action, …)]`** — *per-action* policy (the only place per-action policy lives): `Timeout`, `MaxAttempts`, `OnFailure`, `Lane` (defaults to the action name), `MaxConcurrency`, `Schedule`. Type-level defaults apply where an action is unspecified.
- **`[JobChain(a, b, c, d)]`** — type-level **linear** pipeline; on a step's success the orchestrator persists the (mutated) work-item and auto-advances to the next stage **unless** the handler called `ctx.StopChain()`/`ctx.ContinueWith()`.
- **`[JobIdempotent(keys…)]`** — declared idempotency/coalesce key `(type, keys…, action)`; re-delivery is deduped, concurrent duplicates coalesce.
- **`[JobPersistence(Auto | InMemory | DataStore)]`** — per-type durability override of the capability election (§8/§16); Wolverine-style durable-vs-buffered.

## 6. Decision — two trigger models

- **Edge-triggered** (`Submit`): a specific work-item gets an action queued, claimed once (atomic CAS), run once. Low latency.
- **Level-triggered** (`[JobAction(Schedule=…)]`): on a timer, sweep **all** work-items resting in the action's state and run the handler. Self-healing, set-based reconciliation (the Kubernetes-controller pattern).

Pairing both = **edge for latency, level for correctness**: a lost edge is eventually healed by the sweep. `Schedule` accepts an **interval** (`"00:10:00"`), a **cron** expression (`"0 2 * * *"`), or **sentinels** (`@boot`, `@continuous`).

**Strategic consequence:** scheduling *generalizes* the three legacy services into one scheduled-sweep engine:
- reaper = built-in sweep(`Running && lease < now`) `@continuous`,
- boot recovery = built-in sweep(`non-terminal`) `@boot`,
- recurring/reconcile = user-declared `Schedule`.

## 6.5 Decision — reschedule & resource gates (cooperative backoff)

A job may be unable to make progress for a **transient external** reason (HTTP 429 + `Retry-After`, a locked resource, an upstream briefly down). This is **not a failure** and must **not** consume a retry attempt — conflating the two dead-letters healthy jobs the moment a cooldown outlasts `MaxAttempts × backoff`.

### Handler outcomes (explicit)
`Execute` signals its outcome by returning normally (success → Completed / advance chain), throwing (failure → retry per policy → `Dead` when exhausted), or calling a control verb on `ctx`:

| Verb | Meaning | Consumes an attempt? |
|---|---|---|
| `ctx.Reschedule(after)` | defer THIS job; re-queue the **same** stage at `now+after` (honor a 429 `Retry-After`) | **No** |
| `ctx.Backoff(after, key=null)` | set a shared **resource gate** for `key` (default = the job's gate key) until `now+after`, AND reschedule this job | **No** |
| `ctx.ContinueWith(action)` / `ctx.StopChain()` | chain control (success path) | n/a |

(A `throw new RescheduleException(after)` form is allowed for code buried in a library that can't reach `ctx`, but the verb is the idiom.)

### The resource gate (shared circuit-breaker)
A work-item declares the resource it contends for via a **gate key the orchestrator can read WITHOUT executing** — `[JobGate(nameof(Source))]` (or a `string? GateKey` convention for dynamic keys). When a gate is set (by `ctx.Backoff` on a 429), the orchestrator **checks it at dispatch, before running the handler**: any job whose gate key is currently gated is deferred to the gate's `ReleaseAt` *without being run*. So job #100's 429 gates `api:foo` for 5 min, and jobs #101–#1000 with key `api:foo` never hit the API — they defer at dispatch. The handler's own "can I connect?" check is a fallback, not the throttle.

**The gate is capability-graded like the ledger.** In-memory for the Local tier; a shared **gate-state record in the ledger store** (`{gateKey, releaseAt, reason}`) for Durable/Distributed — so node B honors a 429 that node A received (an in-process gate would let every node re-hammer the API). It is not separate infrastructure, just a small keyed record the orchestrator consults at dispatch.

### Thundering herd on release
900 jobs deferred to the same `ReleaseAt` would wake together and re-trigger the 429. Mitigate with **(a) jitter** — spread the deferred `visibleAt` across a window past `ReleaseAt` — and **(b)** the lane **`MaxConcurrency`** cap, which bounds how many run at once regardless. Base the delay on `Retry-After` when present; add jitter for the spread.

### Runaway guards
Because reschedule/backoff deliberately don't consume attempts, a permanently-down resource could defer a job forever. Two optional per-action guards, with a **separate reschedule counter** from the retry `Attempt` counter (Reschedule != Retry, so they cap independently and dead-letter with distinct reasons — `Poison` vs `PerpetuallyDeferred`):
- **`Deadline`** (total wall-clock from first submit) — the **primary** guard; semantically correct ("we've been trying too long").
- **`MaxReschedules`** (deferral count) — a **secondary** guard, default high/off. Count caps are fragile for cooperative backoff (a 2h outage with 5m cooldowns is 24 *legitimate* reschedules, so a low cap dead-letters healthy jobs mid-outage); its real value is catching a pathological `Reschedule(0)` spin faster than a time cap would.

### Stateful escalation
The handler reads `ctx.State.Reschedules` (read-only, §4.2) to escalate its own backoff — the orchestrator never decides the *policy*, only enforces the guards:
```csharp
case Stage.Fetch:
    var res = await Fetch(pkg, ct);
    if (res.StatusCode == 429)                                  // gate the WHOLE api:foo for every job
    {
        if (ctx.State.Reschedules >= 3) ctx.Reschedule(Tomorrow9am());      // escalate: absolute time
        else ctx.Backoff(res.RetryAfter ?? TimeSpan.FromMinutes(5));        // honor Retry-After + gate peers
        return;
    }
    pkg.Raw = res.Body;                                         // success: mutate work-item; [JobChain] advances
    break;
```

## 7. Decision — the orchestrator internals

1. **Ledger is the single source of truth and the single writer.** Every transition goes through the orchestrator as a defined state machine and appends a transition record (audit). One writer ⇒ a coherent "what happened."
2. **The ledger *is* the queue.** Dispatch = **atomically claim** the next ready row (`status=Queued && visibleAt<=now`, CAS `→Running` + stamp lease/owner). No separate volatile queue; nothing to reconcile after a restart.
3. **Claim is in-place CAS, never a cross-partition move** — avoids the non-atomic "vanish" window.
4. **Durable cancellation.** A cancel marker on the ledger entry (cross-process, restart-durable). Pre-running → terminate. Running → cooperative `ct`. Stuck → `Timeout` fires `ct` + the ledger abandons the row (best-effort; **no hard-kill promise** — .NET can't force-abort a `Task`; true kill needs worker-process isolation, out of scope).
5. **Transactional outbox.** Because the ledger lives on the same store as domain data, `Submit` writes the Job in the **same transaction** as the domain write when an ambient transaction exists (enqueue-on-commit) — closes the "saved but never enqueued" gap. **Must be in the enqueue path from day one.**
6. **Physical layout hides behind `IJobLedger`.** Default = **one active store + composite `(status, visibleAt)` index + a terminal sweep** moving completed/failed rows to a cold `archive`/`dead` partition (the sweep is off the hot path, idempotent, crash-safe). Hot/cold partitioning and per-stage partitioning (DATA-0077 + `Entity.Move`) are **opt-in scale layouts** — made safe by level-triggered reconciliation — selected behind the interface without touching user code.

## 8. Decision — capability-graded backend (Reference = Intent)

A capability token + provider election (ARCH-0084, same machinery as the cache pillar) picks the implementations:

| Tier | Trigger | `IJobLedger` | `IJobTransport` | Guarantee |
|---|---|---|---|---|
| **Local** | no data adapter | in-memory | in-proc | at-least-once, **non-durable** (lost on crash) |
| **Durable** | a data adapter present (Mongo/PG/…) | the adapter's store | in-proc | at-least-once, **survives restart** |
| **Distributed** | multiple nodes on the shared durable store | shared store | competing consumers (the **store is the coordinator**) | at-least-once, multi-node |
| **Distributed + push** | `Koan.Messaging` present | shared store | message bus (push/route) | at-least-once, low-latency dispatch |

The **delivery contract is constant** across tiers; only durability/scale change. "Distributed" is mostly free once durable (N nodes claim from the same ledger); the bus is a latency/routing *upgrade*, not a prerequisite. The **resource gate** (§6.5) is graded the same way — in-memory locally, a shared ledger-store record when durable — so cooperative backoff (429 cooldowns) is honored across all nodes, not just the one that hit the limit. The chosen tier is announced in the **boot report**:

```
[Koan.Jobs] ledger=Mongo (durable) · transport=in-proc · distributed=1 node · 6 actions, 2 scheduled, 1 chain · mode=normal
```

## 9. Status lifecycle

`JobStatus` (the lifecycle of a Job/ledger entry):

```
Created ─Submit─▶ Queued ─claim(CAS)─▶ Running ─┬─ ok ───▶ Completed   (or → next chain stage = Queued)
                    ▲                            ├─ fault, retries left ─▶ Queued (future visibleAt)
        block/defer │                            ├─ fault, exhausted ───▶ Failed ─(OnFailure)─▶ chain Abort/Continue
                    │                            ├─ cancel marker ──────▶ Cancelled
              Blocked (deps/rate-gate/wait)      └─ timeout/lease lapse ─▶ (reclaim) or ─▶ Dead (poison)
```

- **Stage** = the action token the work-item rests in (drives level-triggered sweeps and `[JobChain]` advancement).
- **`Dead`** = exhausted/poison terminal; queryable + replayable (`job.Replay()`), retained in the `dead` partition.
- **Reschedule / Backoff** (`ctx.Reschedule`/`ctx.Backoff`, or a resource gate hit at dispatch — §6.5) → back to `Queued` with a future `visibleAt` plus `DeferReason` + `NextAttemptAt` for observability (the dashboard shows e.g. "320 deferred: api:foo rate-limited until 14:05"). It increments a separate `Reschedules` counter and **does not increment `Attempt`** — the load-bearing difference from a retry. The handler can read its own `ctx.State.Reschedules`/`Attempt` to escalate. A perpetually-deferred job dead-letters (`PerpetuallyDeferred`) at its optional `Deadline` (primary) or `MaxReschedules` (secondary).
- Terminal rows (`Completed`/`Failed`/`Cancelled`/`Dead`) are swept to the cold partition; `active` stays lean.

**Worked trace** — `pkg.Submit(Stage.Fetch)` on a 4-stage `[JobChain]`:

```
Submit(Fetch)         ledger: {pkg#42, stage=Fetch,   status=Queued,    attempt=0}     transition: ∅→Queued
claim (CAS)                   {pkg#42, stage=Fetch,   status=Running,   attempt=1, lease=+10m}   Queued→Running
Execute(Fetch) ok →           pkg.Raw set; persist pkg; [JobChain] next=Parse           Running→Completed(Fetch) ; ∅→Queued(Parse)
  {pkg#42, stage=Parse,  status=Queued} … Running … ok → next=Mint
  {pkg#42, stage=Mint,   status=Queued} … Running … ok → next=Publish
  {pkg#42, stage=Publish,status=Queued} … Running … ok → no next
final                         {pkg#42, stage=Publish, status=Completed}                 → swept to archive
```

## 10. Usage examples (ergonomics)

**Simplest — single-action, single-line:**
```csharp
public sealed class ThumbnailJob : Entity<ThumbnailJob>, IKoanJob<ThumbnailJob>
{
    public string SourceUrl { get; set; } = "";
    public string? ThumbUrl { get; set; }
    public static async Task Execute(ThumbnailJob job, JobContext ctx, CancellationToken ct)
        => job.ThumbUrl = await Thumbs.Make(job.SourceUrl, ct);   // no chain, no re-submit → Completed
}

await new ThumbnailJob { SourceUrl = url }.Job.Submit();          // Created→Queued→Running→Completed
```

**Multi-action pipeline (declared chain) + scheduled reconciler + DI:**
```csharp
public static Task Execute(PresetPackage pkg, JobContext ctx, CancellationToken ct) => ctx.Action switch
{
    Stage.PrepareToFetch => Prepare(pkg, ctx, ct),   // scheduled sweep; ctx.ContinueWith(Stage.Fetch) to enter the chain
    Stage.Fetch          => Fetch(pkg, ct),          // [JobChain] auto-advances to Parse on success
    Stage.Parse          => Parse(pkg, ct),
    Stage.Mint           => Mint(pkg, ct),
    Stage.Publish        => Publish(pkg, ctx.Services.GetRequiredService<IPublisher>(), ct),
    _ => Task.CompletedTask,
};

await pkg.Job.Submit(Stage.Fetch);      // edge: kick one (instance accessor)
await packages.Submit(Stage.Fetch);     // edge: bulk-enqueue 1000 (constrained collection extension)
// level: [JobAction(Stage.PrepareToFetch, Schedule="00:10:00")] sweeps every 10m — nothing to call
```

**Manual chaining (no `[JobChain]`):**
```csharp
case Stage.Fetch:
    await Fetch(pkg, ct);
    await pkg.Job.Submit(Stage.Parse);   // explicit next step; branch freely
    break;
```

**Deterministic tests (no container):**
```csharp
services.AddKoanJobs(o => o.Mode = JobMode.Inline);  // Submit executes synchronously; assert the work-item's terminal state
```

## 11. Consequences

### Positive
- One **encapsulated** orchestrator concern; the lifecycle is visible in one place and self-reported in the boot report.
- **Real ledger** (single writer + transition log) ⇒ durable audit, holistic queries ("all Running"), and a near-free Hangfire-style dashboard.
- **Real cancellation** (durable, cross-process) and a declarative stuck-job trigger (`Timeout`).
- **Fewer parts**: the queue, 4 background services, the reflection fan-out, and the ambient statics collapse into `JobOrchestrator` + `IJobLedger` + a scheduled-sweep engine + a handler registry.
- **Self-healing** (level-triggered) + **low-latency** (edge-triggered).
- **Testable in isolation** (inline mode; injected collaborators behind interfaces).
- Pure-data work-items ⇒ serializable, distributable; no behavior welded to persisted state.

### Negative
- Greenfield rewrite: the entire current module is deleted; in-flight dogfeed data is re-seeded (no migration).
- `Timeout` cannot hard-kill a runaway handler (cooperative + ledger-abandon only).
- Behavior on a `static abstract` member uses `ctx.Services` for DI (service-location) unless a handler class is registered.
- A single canonical ledger departs from table-per-type (JOBS-0003) — deliberate; it's what enables holistic queries and kills the reflection fan-out.

### Neutral
- Per-adapter ledger conformance + edge/level/chain/idempotency/cancel/distributed behavior become mandatory live tests (ARCH-0079).

## 12. Resolved decisions (Accepted 2026-06-04)

All positions below are **adopted as the decision** (each proposed/recommended option). Architect's explicit picks on the taste calls: **#4 → `Submit`** · **#5 → keep the `ct` parameter** · **#13 → optional `Deadline` + `MaxReschedules`, with a 24h default `Deadline`** · **#14 → accessor naming `Job` (instance: "the article's job") / `Jobs` (static type-level facade: "the Article jobs subsystem"), implemented via source generator.** The numbered list is retained as the rationale of record.

1. **Chain/manual collision rule** — *Proposed:* declared `[JobChain]` auto-advances on success **unless** the handler calls `ctx.StopChain()`/`ctx.ContinueWith(action)` (which replaces the default next step). **Recommend: accept.**
2. **`OnFailure` default** — *Proposed:* **`Abort`** (fail-stop), applied **after** retries are exhausted; `Continue` is opt-in. **Recommend: accept.**
3. **`Timeout` semantics** — *Proposed:* cooperative-cancel + ledger-abandon, **per-action**, **no hard-kill promise**. **Recommend: accept** (document the limitation).
4. **Verb** — `Submit` vs `Enqueue`. *Proposed:* **`Submit`** (tier-agnostic; "enqueue" leaks the queue mechanism that tiers 2–4 don't use). **Recommend: `Submit`** — but `Enqueue` is the familiar fallback if you prefer discoverability.
5. **Signature** — fold `ct` into `ctx.Cancellation` (`Execute(job, ctx)`), or keep the `ct` parameter? *Proposed:* **keep `ct` as a parameter** (the .NET idiom analyzers expect). Minor.
6. **Action token type** — `enum` vs `static class … const string`. *Proposed:* **either, but persisted by NAME** in the ledger (never enum ordinals — reorder = silent corruption). **Recommend: `static class` consts** (open/extensible without recompiling an enum) or enum-by-name.
7. **Default physical layout** — *Proposed:* **single active store + composite index + terminal sweep**; hot/cold and per-stage partitioning are opt-in scale knobs behind `IJobLedger`. **Recommend: accept** (don't ship per-state moves in v1).
8. **v1 "can't-retrofit" commitments** — *Proposed:* commit to **transactional outbox**, **unified queryable ledger**, **declared idempotency**, **boot-report visibility**, **inline test mode** in v1 (the rest — recurring beyond `Schedule`, dead-letter replay UX, multi-tenant partitions — are seams). **Recommend: accept.**
9. **DI in handlers** — *Proposed:* `ctx.Services` default + optional `IKoanJobHandler<T>` class for DI-heavy jobs. **Recommend: accept.**
10. **`JobHandle.Completion` in the distributed tier** — awaiting a result across nodes needs the ledger polled or a completion signal on the bus. *Proposed:* support `Completion(timeout)` always; back it with ledger polling, upgrade to a push signal when the bus is present. **Recommend: accept.**
11. **Gate-key declaration** — `[JobGate(nameof(Source))]` (declarative, readable at dispatch) vs a dynamic `string? GateKey` convention vs both. *Proposed:* **both** — attribute for the common static case, `GateKey` override for per-tenant/per-host dynamic keys. **Recommend: accept.**
12. **Reschedule honoring + jitter** — *Proposed:* `ctx.Reschedule`/`Backoff` honor the supplied delay (e.g. a 429 `Retry-After`) as the base, and the orchestrator adds **release jitter** (spread `visibleAt` past `ReleaseAt`) so a deferred herd doesn't wake in lockstep; lane `MaxConcurrency` is the second line of defense. **Recommend: accept** (jitter default ~a few seconds or ±10% of the delay — to be tuned).
13. **Runaway guards (`Deadline` + `MaxReschedules`)** — *Proposed:* `Deadline` (total wall-clock) is the **primary** guard; `MaxReschedules` (count) is a **secondary**, default-high/off spin-guard; the reschedule counter is **separate** from the retry `Attempt` counter. *Open:* whether `Deadline` should have a sane default cap (e.g. 24h) so a forgotten `Deadline` can't accumulate infinitely-deferred jobs. **Recommend: accept both as optional; lean toward a 24h default `Deadline`.**
14. **Accessor surface (`.Job` / `.Jobs`)** — instance `model.Job.Submit()` + static facade `MyModel.Jobs.Cancel(id)`/`.Where(…)` + constrained collection `list.Submit(action)`. **Hard C# constraint: a static and an instance member cannot share the name `Job`** (CS0102) — hence singular instance `Job` + plural static `Jobs`. *Open:* implement via **source generator** (emits `partial` members per `IKoanJob` type — safe, on-brand, needs `partial`) vs **C# 14 / .NET 10 extension properties** (no `partial`, but verify the generic-constrained shape compiles). **Recommend: source generator**; also reserve/diagnose the `Job` name vs a domain property called `Job`. Handlers receive a read-only `ctx.State` (`JobState`) for stateful decisions (decided, not open). **Resolved at implementation (§16): C# 14 extension members — both `model.Job` and `MyModel.Jobs` compile, so no source generator is used.**

## 13. Test surface (a first-class deliverable; ARCH-0079 integration-as-canon)

Testing is **part of the deliverable**, not an afterthought. The defining property: **the same behavioral suite runs unchanged across every tier** — that *is* the proof of the constant at-least-once + idempotent contract (§2.5): identical assertions must pass in-memory, on each real DB, and distributed. Three pieces of test infrastructure make it deterministic and exhaustive:

- **Virtual clock.** Schedules, timeouts, backoff / `Retry-After`, lease expiry, and `Deadline` are driven by an injectable clock — tests *advance time* instead of sleeping (no flakiness, no real waits). A 24h `Deadline` or a 10-min `Schedule` is a clock tick.
- **Crash/restart harness.** The orchestrator can be killed mid-run and rebooted against the same ledger — simulating SIGTERM, hard crash, and a worker dying with the lease held — to prove **stop → interruption → pick-up**.
- **Inline mode.** `Submit` executes synchronously on the caller (no worker, no clock) for unit-style assertions on a handler's logic and chain decisions, with zero infrastructure.

### A. In-memory tier (dockerless — the bulk of behavioral coverage, runs on every build)
- **Edge**: `Submit` single + 1000-batch → all `Completed`; `[JobIdempotent]` dedupes re-delivery; concurrent duplicates coalesce.
- **Level**: a `[JobAction(Schedule)]` sweep (advanced via the virtual clock) picks every work-item resting in the stage; a dropped edge is **healed by the next sweep**; a large backlog sweep is bounded + resumable across ticks; concurrent sweepers never double-process (per-record CAS).
- **Chain**: `[JobChain]` advances linearly, **persisting the mutated work-item between steps**; `OnFailure=Abort` stops, `Continue` proceeds; `ctx.ContinueWith` branches; `ctx.StopChain` ends early.
- **Reschedule / backoff / gate** (§6.5): `ctx.Reschedule(after|until)` re-queues **without consuming `Attempt`**; a 429 `ctx.Backoff` gates the resource so peer jobs defer **at dispatch without running**; stateful escalation via `ctx.State.Reschedules`; `Deadline`/`MaxReschedules` dead-letter to `Dead(PerpetuallyDeferred)`.
- **Cancel / Timeout**: durable cancel terminates pre-running and cooperatively cancels running; `Timeout` (virtual clock) abandons a stuck job; reschedule-count vs retry-`Attempt` tracked independently.
- **Lanes**: per-action/lane `MaxConcurrency` honored; one slow job never head-of-line-blocks its lane peers.
- **Outbox**: a `Submit` inside a **rolled-back** ambient transaction never dispatches; on commit it dispatches exactly once.

### B. Durable tier — stop / interruption / pick-up, on EACH database
The identical suite re-runs against **every data adapter — Mongo, Postgres, SqlServer, SQLite, + InMemory-as-oracle** — proving the ledger contract holds on each store (claim CAS atomicity, transition atomicity, the gate as a shared record, the terminal sweep, any hot/cold or per-stage partition layout). Plus the recovery scenarios only a real store can prove:
- **Hard crash mid-run** → reboot → a `Running` job whose lease lapsed is reclaimed and re-run **idempotently** (no double-effect); nothing lost.
- **Graceful stop (SIGTERM)** → in-flight jobs drain; un-started `Queued` jobs survive and run after restart.
- **Worker dies holding the lease** → another (or the rebooted) worker reclaims at lease expiry; the original's late write is **rejected (stale-lease guard)** — no double-run.
- **Restart loses nothing**: there is no volatile queue to lose — the ledger is the truth; boot recovery is the `@boot` sweep claiming whatever is ready.
- **Mid-chain interruption**: a crash between two chain stages resumes at the correct stage (the persisted work-item + ledger stage are the resume point).
- **Layout**: terminal rows are swept to cold so `active` stays bounded; a per-stage partition layout (if enabled) reconciles correctly under level-triggering.

### C. Distributed tier — N-node competing consumers (shared store)
- No double-claim under contention (CAS); fair-ish work-stealing across nodes.
- The resource gate is honored **cross-node** (node B does not hammer an API node A was 429'd on) — the old in-memory-gate behavior is an explicit **regression** test.
- `JobHandle.Completion` resolves on a node that didn't submit (ledger poll; bus push when present).
- **+bus** variant (`Koan.Messaging`): push-dispatch latency without changing the contract.

### D. Scale / soak
- 1000-job batch drains within bounded concurrency; the ledger `active` set stays lean (terminal sweep keeps up).
- Gate-release of a deferred herd does **not** re-trigger the limit (jitter + lane cap measured).

Every adapter and the orchestrator core ship at least one spec through real `AddKoan()` reflective discovery (ARCH-0079); the in-memory tier doubles as the convergence oracle the durable tiers are asserted against.

## 14. Migration

Greenfield: delete `Koan.Jobs.Core`, re-author against `IKoanJob<T>`. Dogfeed re-seeds. No data migration (pre-1.0). **Flagged.**

## 15. Prior art

- **Wolverine** (critter stack) — handler convention, graded local/durable/external transports with a constant at-least-once contract, durable inbox/outbox. Closest overall match.
- **Temporal** — task queues, durable execution, workers register handlers; the reference for level-triggered + durable.
- **Hangfire** — storage-graded enqueue, named queues, dashboard/ledger. The dashboard is the adoption lever this design's ledger enables.
- **MassTransit** — transport abstraction, competing consumers, type routing.
- **Kubernetes controllers / operators** — level-triggered reconciliation (the `Schedule` model); edge-for-latency, level-for-correctness.
- **Azure Durable Functions** — function chaining (the `[JobChain]` shape).
- **Sidekiq / Coravel** — per-state Redis structures; minimal in-proc queue (the Local tier's smallness).

## 16. Implementation status (2026-06-04)

Built as a single project **`src/Koan.Jobs`** (no Abstractions/Core split — extract only if an external adapter later needs the contracts). The in-memory and durable tiers are complete and verified; the distributed tier and the per-DB container matrix follow.

**Shipped & verified**
- **In-memory tier** — the full engine (claim/execute/settle/chain/reschedule/backoff/gate/cancel/timeout/lanes/reap/schedule) behind `IJobLedger`, with **21 behavioral specs green** via a `FakeTimeProvider` harness.
- **Durable tier** — `DataJobLedger` over `Entity<JobRecord>` plus parallel `JobGateRecord` and `JobClaimTicket` sets (no per-DB job adapters; durability follows the ambient data adapter). Capability election picks it when a durable adapter is present. **Verified on a live SQLite store (4 green)**: election, durable persist + complete, claim-query translation, chain advance, lease reclaim.

**Refinements adopted during implementation** (these supersede the proposed text where they differ)
- **Accessor → C# 14 extension members.** Resolves Open Q §12.14: both `model.Job` (instance) and `MyModel.Jobs` (static) compile as extension members, so **no source generator** is used — one fewer moving part.
- **`[JobPersistence(Auto | InMemory | DataStore)]`** added (§5) — per-type durability override (Wolverine-style durable-vs-buffered). A `Provider` pin to a named store is reserved.
- **`JobsOptions.ClaimStrategy`** — the durable claim is graded: `Optimistic` (default; last-write-wins under the at-least-once + idempotent contract) and `Ticket` (a leaderless GUIDv7 "bakery" election over the parallel ticket set — adapter-generic including Mongo, probabilistic, NTP-dependent). The hard-guarantee **`NativeCas`** tier is deferred to a future Koan.Data primitive (a generic conditional-update / `ExecuteUpdate`-style capability) plus a consensus/sync module.
- **Clock = `System.TimeProvider`** (no bespoke `IClock`); `FakeTimeProvider` drives deterministic tests.
- **Scheduling is an initiator concern, not a job state.** A scheduled action does not park; the scheduler submits a fresh job on the cadence (TimeSpan interval / cron via Cronos / `@boot` / `@continuous`) against the per-type singleton, and recurrence comes from re-submitting (overlap coalesces via `[JobIdempotent]`). `MyModel.Jobs.Trigger(action)` is the on-demand twin (a type-level singleton submit).
- **Chain advance** = settle the current `JobRecord` Completed and append a **new** `JobRecord` for the next stage (one ledger entry per stage; the work-item carries state forward by id).

**Remaining**
- **Crash-recovery — shipped** (SQLite, 3 specs): rebooting a harness against the same store proves queued work survives a restart, a lapsed-lease job is reclaimed, and a mid-chain crash resumes at the next stage (the ledger is the truth; nothing lost).
- **Per-DB container matrix** — infrastructure is in place (the convergence suite + `JobsHarness.StartWithSettingsAsync` make each tier a subclass + connector reference). Enabling Postgres/Mongo/SqlServer is **blocked on a framework gap**: the durable Jobs ledger's framework-defined entities (`JobRecord`/`JobGateRecord`/`JobClaimTicket`) are not schema-ensured/created on the Postgres adapter (SQLite auto-creates on write; PG's `data.ensureCreated` for these entities does not materialize the table). That's a Koan data-layer/schema concern, not a Jobs bug — once it's closed, each tier is a one-class subclass.
- **Convergence suite — shipped**: the behavioral contract lives in `Koan.Jobs.TestKit`'s `JobBehaviorSuite` (28 specs); each tier is a thin subclass providing a harness, so the *same* assertions run on in-memory and on a real SQLite store (ARCH-0079). Adding a per-adapter tier is now just a subclass + connector reference.
- The distributed tier: competing-consumers test, cross-node gate, the `+bus` transport package, and per-type `[JobPersistence]` two-ledger routing.
- **Transactional outbox — shipped** (automatic): on the durable tier a `Submit` inside an ambient transaction enlists (`TrackSave`) and is enqueued on commit / discarded on rollback; inline mode skips its synchronous drain inside a transaction.
- **Terminal archival — shipped**: a worker sweep (`ArchiveInterval`) purges Completed/Cancelled rows older than `ArchiveAfter` (default 7d) via `IJobLedger.PurgeArchivable`; Failed/Dead are retained (replayable).
- Boot-report polish.

**Authoring guide:** [Background Jobs How-To](../guides/jobs-howto.md).
