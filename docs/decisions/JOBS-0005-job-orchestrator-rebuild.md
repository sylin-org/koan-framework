# JOBS-0005: Job Orchestrator rebuild — a durable, edge+level-triggered state machine

**Status**: **Accepted (2026-06-04)** · **Implemented — in-memory + durable tiers, SQLite-verified (2026-06-04; see §16)**. All positions ratified by the architect (§12). Greenfield rebuild: **supersedes JOBS-0001, JOBS-0002, JOBS-0003** and the in-code "JOBS-0004" partition tier. The current `Koan.Jobs.Core` module is discarded.
**Date**: 2026-06-04
**Deciders**: Enterprise Architect
**Scope**: Replace the scattered job subsystem with a single, encapsulated **Job Orchestrator** concern that owns a ledger, dispatch/recall, cancellation, and scheduling. Define the entity-first authoring surface, the capability-graded backend, and the state-machine execution model (edge-triggered `Submit` + level-triggered `Schedule`).
**Related**: supersedes JOBS-0001/0002/0003 · DATA-0077 (partitions / `EntityContext.With`) · Entity Transfer `Entity<T>.Move()` · DATA-0098 (boundary encoding) · ARCH-0084 (capability tokens + provider election) · ARCH-0086 (`KoanModule` bootstrap + `Report`) · `[KoanDiscoverable]` + `KoanRegistry.GetDiscoveredImplementors` (auto-registration) · ARCH-0113 (Communication-owned internal signals) · ARCH-0079 (integration tests as canon) · the Koan redesign initiative ("fewer but more meaningful parts").

> **Implementation update (2026-07-15):** ARCH-0113/R07-11a removes the public `IJobTransport`,
> `InProcessJobTransport`, and `Koan.Jobs.Transport.Messaging` bridge. `JobWakeCoordinator` now emits
> one bounded internal Communication signal. The built-in provider supplies local wake automatically;
> a directly referenced Communication connector transparently changes reach. The signal remains a
> lossy latency hint, carries neither work nor business context, and never replaces ledger polling.

> **Implementation update (R07-14, 2026-07-16):** finite and asynchronous Entity sources now submit
> pointwise through the same coordinator acceptance operation as scalar `.Job.Submit()`. One terminal
> captures/restores logical context, accepts items sequentially, emits bounded wake hints, and returns
> a fixed-size `JobSubmission`. Typed failure/cancellation preserve the confirmed accepted prefix.
> The duplicate type-level list submit, unbounded record materialization, and opaque `int` result are
> removed. This is ordered fan-out, not collection atomicity; streaming bounds producer memory, not
> ledger growth.

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
| **Wake hint** | an internal Communication signal that may shorten the next ledger poll; never a Jobs provider or application API. |

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

Job operations live under a `.Job` (instance) / `.Jobs` (static) accessor so they never pollute the model's domain surface or the data verbs (`Save`/`Get`/`Query`/`Delete`). `model.Job` = "this instance's job ops"; `MyModel.Jobs` = "the job control plane for this type" (trigger, by-id status/cancel, ledger queries). Direct Entity sources own pointwise submission. See Open Q §12.14 for the accessor mechanics and the static-vs-instance constraint.

```csharp
// single — instance accessor
await new ThumbnailJob { SourceUrl = url }.Job.Submit();          // single-action: no action arg
await pkg.Job.Submit(Stage.Fetch);                               // multi-action
await pkg.Job.Submit(Stage.Fetch, after: TimeSpan.FromMinutes(5)); // delayed

// source — pointwise ledger acceptance with one fixed-size result
JobSubmission submitted = await packages.Submit(Stage.Fetch);

// type-level facade — by-id, ledger queries
await PresetPackage.Jobs.Status(id);
var running = await PresetPackage.Jobs.WithStatus(JobStatus.Running);
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

A capability decision selects the ledger; Communication independently elects physical carriage for
the optional internal wake hint:

| Tier | Trigger | `IJobLedger` | Wake hint | Guarantee |
|---|---|---|---|---|
| **Local** | no data adapter | in-memory | built-in Communication provider | at-least-once, **non-durable** (lost on crash) |
| **Durable** | a data adapter present (Mongo/PG/…) | the adapter's store | built-in Communication provider | at-least-once, **survives restart** |
| **Distributed** | multiple nodes on the shared durable store | shared store | competing consumers (the **store is the coordinator**) | at-least-once, multi-node |
| **Distributed + push** | a Communication connector is directly elected | shared store | stable internal worker group | at-least-once, low-latency dispatch |

The **delivery contract is constant** across tiers; only durability/scale change. "Distributed" is mostly free once durable (N nodes claim from the same ledger); Communication reach is a latency *upgrade*, not a prerequisite. The **resource gate** (§6.5) is graded the same way — in-memory locally, a shared ledger-store record when durable — so cooperative backoff (429 cooldowns) is honored across all nodes, not just the one that hit the limit. The chosen ledger and wake provider are announced in shared facts:

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
await packages.Submit(Stage.Fetch);     // edge: pointwise source submission (constrained Entity extension)
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
10. **`JobHandle.Completion` in the distributed tier** — awaiting a result across nodes needs the ledger polled or a completion signal. *Proposed:* support `Completion(timeout)` always; back it with ledger polling, with a future internal Communication signal as a latency upgrade. **Recommend: accept.**
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
- **Communication-signal** variant: cross-node wake latency through the elected adapter without changing the ledger contract.

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

Built as a single project **`src/Koan.Jobs`** (no Abstractions/Core split). Communication owns the
optional physical wake mesh, so Jobs has no transport package or provider seam. **The full capability
ladder is complete and green: in-memory → durable → per-DB container matrix
(SQLite/Postgres/Mongo/SqlServer) → distributed → Communication-backed wake.** Every item below ships
with tests; the detailed status follows.

**Shipped & verified** (the same behavioral suite — `JobBehaviorSuite`, 28 specs — runs across every tier per ARCH-0079)
- **In-memory tier** — the full engine (claim/execute/settle/chain/reschedule/backoff/gate/cancel/timeout/lanes/reap/schedule/archival) behind `IJobLedger`, driven by a `FakeTimeProvider` harness. **In-memory project: 32 green** (28 convergence + 2 distributed + 2 transport).
- **Durable tier** — `DataJobLedger` over `Entity<JobRecord>` plus parallel `JobGateRecord` and `JobClaimTicket` sets (no per-DB job adapters; durability follows the ambient data adapter). Capability election picks it when a durable adapter is present. **Verified on live stores:** SQLite (35: 28 convergence + 3 durable-specific + 3 crash-recovery + 1 routing), **Postgres 28**, **Mongo 28**, **SQL Server 28** — all the same suite.

**Refinements adopted during implementation** (these supersede the proposed text where they differ)
- **Accessor → C# 14 extension members.** Resolves Open Q §12.14: both `model.Job` (instance) and `MyModel.Jobs` (static) compile as extension members, so **no source generator** is used — one fewer moving part.
- **`[JobPersistence(Auto | InMemory | DataStore)]`** added (§5) — per-type durability override (Wolverine-style durable-vs-buffered). A `Provider` pin to a named store is reserved.
- **`JobsOptions.ClaimStrategy`** — the durable claim is graded: `Optimistic` (default; last-write-wins under the at-least-once + idempotent contract) and `Ticket` (a leaderless GUIDv7 "bakery" election over the parallel ticket set — adapter-generic including Mongo, probabilistic, NTP-dependent). The hard-guarantee **`NativeCas`** tier is deferred to a future Koan.Data primitive (a generic conditional-update / `ExecuteUpdate`-style capability) plus a consensus/sync module.
- **Clock = `System.TimeProvider`** (no bespoke `IClock`); `FakeTimeProvider` drives deterministic tests.
- **Scheduling is an initiator concern, not a job state.** A scheduled action does not park; the scheduler submits a fresh job on the cadence (TimeSpan interval / cron via Cronos / `@boot` / `@continuous`) against the per-type singleton, and recurrence comes from re-submitting (overlap coalesces via `[JobIdempotent]`). `MyModel.Jobs.Trigger(action)` is the on-demand twin (a type-level singleton submit).
- **Chain advance** = settle the current `JobRecord` Completed and append a **new** `JobRecord` for the next stage (one ledger entry per stage; the work-item carries state forward by id).

**Remaining**
- **Crash-recovery — shipped** (SQLite, 3 specs): rebooting a harness against the same store proves queued work survives a restart, a lapsed-lease job is reclaimed, and a mid-chain crash resumes at the next stage (the ledger is the truth; nothing lost).
- **Per-DB container matrix — shipped** (28 specs each, real containers): the same convergence suite runs green on **Postgres** (`PostgresBehaviors`), **MongoDB** (`MongoBehaviors`), and **SQL Server** (`SqlServerBehaviors`) — each a one-class `JobBehaviorSuite` subclass + a Testcontainers fixture, per `JobsHarness.StartWithSettingsAsync`. Total durable convergence: SQLite 28 + PG 28 + Mongo 28 + SqlServer 28, plus SQLite's 3 durable-specific + 3 crash specs.
  - *Root cause of the earlier block (resolved):* it was **not** a Jobs/schema bug. `KoanIntegrationHost` (the ARCH-0079 canonical test host) built a bare `HostBuilder`, whose `IHostEnvironment` defaults to **Production**. The relational DDL guard (`IsDdlAllowed = DdlPolicy==AutoCreate && (!KoanEnv.IsProduction || AllowProductionDdl)`) then refused to auto-create tables, so PG/SqlServer never materialized `JobRecord`/`JobGateRecord`/`JobClaimTicket`. (SQLite slipped through because its bridge configurator ORs `AllowProductionDdl = … || DdlPolicy==AutoCreate`, so SQLite always allows DDL.) Fix: `KoanIntegrationHost` now defaults to a neutral `Test` environment (non-production → DDL allowed; not `Development` → no self-orchestration), with a `WithEnvironment(...)` override — a latent trap closed for **every** future durable-adapter integration suite, not just Jobs.
  - *Mongo specifics:* the document store needs the adapter-specific connection keys (`Koan:Data:Mongo:ConnectionString`/`Database`), and per-boot readiness gating is disabled in the fixture (`Koan:Data:Mongo:Readiness:EnableReadinessGating=false`) since Testcontainers already waits for the container — without it, 28 rapid host boot/dispose cycles intermittently exceed the 30s readiness window (8s green vs 4 min + flaky). SqlServer uses the same readiness-gating opt-out.
- **Convergence suite — shipped**: the behavioral contract lives in `Koan.Jobs.TestKit`'s `JobBehaviorSuite` (28 specs); each tier is a thin subclass providing a harness, so the *same* assertions run on in-memory and on a real SQLite store (ARCH-0079). Adding a per-adapter tier is now just a subclass + connector reference.
- **Distributed core — shipped** (2 specs): two orchestrators ("nodes") share one ledger — competing consumers never double-claim (50 jobs, 2 nodes, exactly 50 runs), and a resource gate set by one node is honored by the other.
- **`[JobPersistence]` two-ledger routing — shipped** (1 spec): when a durable adapter is present, the election yields a `RoutingJobLedger` (public) wrapping `{InMemoryJobLedger, DataJobLedger}`. Per-record ops route by `WorkType → binding.Persistence` (`InMemory` → volatile; `Auto`/`DataStore` → durable); queue-wide reads union both; `ClaimNext` claims from both (disjoint sets); **resource gates are mirrored to both ledgers** so a cooperative backoff holds across persistence tiers. The orchestrator stays storage-agnostic (still one `IJobLedger`). Verified on SQLite: an `InMemory`-typed job leaves no durable `JobRecord` row yet runs, while a `DataStore`-typed job persists.
- **Communication-backed wake — shipped, rebuilt under ARCH-0113**: `JobWakeCoordinator` replaces the worker's fixed poll delay with `WaitForWork(PollInterval)` and emits a bounded internal signal after a non-transactional submit. The built-in Communication provider coalesces locally; a directly elected connector such as RabbitMQ carries the same stable worker-group signal across nodes. There is no public Jobs transport or arbitrary-object bus. The ledger stays the truth—a dropped or duplicated signal costs at most one poll interval, never correctness. Tests: Jobs 77/77 including local wake/coalescing/submission; Communication 31/31; real RabbitMQ 6/6 including the internal signal lane.
- **Transactional outbox — shipped** (automatic): on the durable tier a `Submit` inside an ambient transaction enlists (`TrackSave`) and is enqueued on commit / discarded on rollback; inline mode skips its synchronous drain inside a transaction.
- **Terminal archival — shipped**: a worker sweep (`ArchiveInterval`) purges Completed/Cancelled rows older than `ArchiveAfter` (default 7d) via `IJobLedger.PurgeArchivable`; Failed/Dead are retained (replayable).
- **Boot-report — shipped**: `KoanJobsModule.Report` publishes the discovered job-type count (`jobs.types`); the worker logs a startup summary `[Koan.Jobs] ledger={ledger} · {N} job types · {M} scheduled · claim={strategy}` (the runtime-elected ledger is only available there, not in `Report`). Bootstrap spec asserts both reads resolve and never throw through real `AddKoan()`.

**Authoring guide:** [Background Jobs How-To](../guides/jobs-howto.md).

## 17. Addendum (2026-06-05): work-item write safety

Dogfooding (downstream consumer) surfaced two ways a handler can silently lose a write. Both are framework concerns, not app concerns, and the fixes make *"an entity is a consistency unit"* — the promise entity-first quietly makes — true by default. Two layers: one protects a single handler from itself; one serializes concurrent handlers on the same entity.

### 17.1 Conditional auto-save (dirty-detection)

**Problem.** The orchestrator loads the work-item fresh at dispatch (`binding.Load`), runs `Execute`, then auto-saves the work-item on success (`binding.Save(workItem)`). A handler that loads its *own* second copy of the same entity, mutates it, and saves it — then returns — has its write **clobbered** by the framework's auto-save of the *original* (unmutated) reference. A silent lost write. (Note: this is a stale **write**, not a stale read — "pull latest before handling" already happens and does not address it.)

**Decision.** Auto-save becomes **conditional**: snapshot the work-item's serialized form right after `Load`, and at settle save **only if the in-memory reference actually changed**. A handler that worked on its own copy left `workItem` clean → the framework no-ops → its write stands.

**Mechanism.** Serialize the loaded `workItem` to a deterministic string (System.Text.Json over the entity's public state) and keep it (or its hash). At `SettleSuccessAsync`, re-serialize and compare; skip `binding.Save` when equal. The comparison is *internal* (load vs. settle, same serializer), so it needs only determinism + public-state coverage — it does not need to match the data layer's wire format. Serialization is wrapped so an exotic/cyclic entity that can't be snapshotted **degrades to always-save** (the prior behavior) rather than failing the job. Failure directions are asymmetric: a spurious "dirty" costs one redundant write; a real change can never serialize identically under a deterministic serializer, so a change is never missed.

**Consequences.** The 80% ergonomic (mutate the passed reference → it persists) is unchanged. Pure side-effect handlers (mutate nothing) stop emitting no-op writes. Chain advance is unaffected — a clean work-item means the next stage loads the already-persisted state. Escape hatch reserved for handlers that fully own persistence (currently: just don't mutate the reference; an explicit opt-out attribute can be added if a real case appears). **Boundary:** this protects a single handler against its own double-write; it does *not* protect two *different* handlers racing on the same entity — that is 17.2.

### 17.2 Per-entity serialization by default (`[ParallelSafe]` opt-out)

**Problem.** The orchestrator serializes per `(Lane, MaxConcurrency)` and coalesces per `(action, IdempotentKey)`, but two *different* actions on the same entity (e.g. `FetchPreview` + `DriftCheck` on one `Package`) can run concurrently — each loads, mutates its own copy, and saves; last-writer-wins. 17.1 cannot fix this (both copies were legitimately mutated).

**Decision.** A job's work-item id is its **ordering key** — same key is processed **one at a time** (the Kafka-partition / SQS-FIFO-group model), **by default**. This is the entity-first / actor-lineage default (Temporal, Akka serialize per-id; queue systems default parallel + opt-in lock — Koan.Jobs, being entity-first, takes the former). Opt out per work-type with **`[ParallelSafe]`** — an *assertion* by the author that the type's actions are independent and may run concurrently on one instance. Because the default *is* serialization, there is no `[JobExclusive]` attribute (exclusivity is the unnamed baseline) — the only marker is the rare opt-out.

**Mechanism.** Each `JobRecord` carries an `Exclusive` flag (`true` by default; `false` for `[ParallelSafe]` types), set by `JobRecordFactory`. `ClaimNext` gains one skip predicate alongside the existing lane / gate / cancel ones: **an exclusive candidate is not claimable while any job for the same `(WorkType, WorkId)` is `Running`.** In-memory: a running-set check; durable: a `Status==Running` probe folded into `SelectCandidate`. No new gate type — gates are time-released backoff; this is settle-released. Since `[ParallelSafe]` is per-type and an entity has exactly one type, an entity's jobs are uniformly exclusive or uniformly parallel-safe, so the predicate needs only the candidate's own flag (no reader/writer mixing).

**Guarantee.** Intra-node: strict. Cross-node: **claim-strength** — two nodes can still claim two actions for one entity in the same instant (both probe "any Running?" pre-commit), the same probabilistic window as the claim itself. A hard cross-node guarantee awaits the deferred `NativeCas` / entity-lease primitive; this is consistent with the existing `ClaimStrategy` grading (best-effort now, hard later). Composes cleanly: two chain *runs* on one entity serialize by default, `[JobIdempotent]` still coalesces duplicate triggers ahead of that, and different entities parallelize fully.

**Status:** both shipped 2026-06-05 with specs in `JobBehaviorSuite` (proven across all five tiers — in-memory, SQLite, Postgres, Mongo, SQL Server).

## 18. Addendum (2026-06-05): runtime-resolved gate keys

Surfaced by a downstream consumer proposal (cooperative gating when the gate key isn't a property on the work-item). The cooperative-deferral substrate is otherwise complete; only the *shape of the key* was too rigid.

**Problem.** `[JobGate(property)]` reads one public property at submit and freezes it onto `JobRecord.GateKey`; the dispatch match (`ClaimNext`) compares that frozen string against live `JobGateRecord`s. This is correct when the gate identity is a value the entity already carries (`Source`, `Provider`). It fails when the identity must be **derived** — a related entity loaded at runtime, a brand→host registry lookup, a host parsed off a navigation property. The downstream consumer `Mirror` has no `Host` property (the host lives on the navigated `Package.UpstreamArchive`), so no property name yields `xivmodarchive.com` at submit, and queued siblings can't carry a key that matches the dynamic key one of them writes via `ctx.Backoff`. (The **write** side was already dynamic — `ctx.Backoff(after, key)` — so the gap was purely the read/sibling side.)

**Decision.** `[JobGate]` may name a **method** as well as a property. A method-form gate is an **async, DI-capable resolver** — `Task<string?> Name(IServiceProvider sp, CancellationToken ct)` — invoked at **submit**, its result frozen onto `JobRecord.GateKey` exactly as the property form. Resolution stays at submit (not dispatch) for two reasons: it keeps the cheap frozen-key dispatch match unchanged, and it makes the key **canonical** — the writer's `ctx.Backoff()` default key and every sibling's `GateKey` derive from the same resolver, so they agree by construction (no write/read key-shape drift). The gate identity for an HTTP host / provider is stable, not late-arriving, so submit-time resolution is sufficient.

**Mechanism.** The binder dispatches on member kind: a **property** binds the existing sync getter; a **method** binds an open delegate `(T, IServiceProvider, CancellationToken) → Task<string?>` at bootstrap (no per-dispatch reflection), validated against that exact signature. `JobRecordFactory.Create` now takes the resolved key as a parameter; `JobCoordinator` resolves it at submit **inside a DI scope** (so resolvers may use scoped services) and stamps it. Chain stages **inherit** the first stage's resolved key (the chain's gate pool is fixed at submit) rather than re-resolving per stage — simpler and correct for stable gate identities. `ClaimNext` and the ledgers are unchanged.

**Three capability levels, additive:** `[JobGate(nameof(Source))]` (a value) → `[JobGate(nameof(HostGate))]` with `HostGate => $"provider:{ProviderId}"` (a computed property — sync, self-only — already worked) → `[JobGate(nameof(ResolveHostGate))]` with `Task<string?> ResolveHostGate(IServiceProvider, CancellationToken)` (loads / looks up — new). The first two are unchanged.

**Not now (YAGNI):** dispatch-time re-resolution — the only thing submit-time can't express — is needed only for keys that *change after queueing* (e.g. a call-time-selected AI-fleet endpoint). It would require moving the gate check after `binding.Load`; deferred until a real late-binding case appears. No HTTP/Retry-After/token-bucket vocabulary enters Koan (the consumer owns that).

**Status:** shipped 2026-06-05 with a spec in `JobBehaviorSuite` (a resolver loads a related entity; one host's `Backoff` defers only same-host siblings at dispatch while other hosts keep running; gate release resumes them — proven across all five tiers).

## 19. Addendum (2026-06-10): high-throughput / bulk — ledger queries, retention, and "chunk, don't multiply"

Surfaced by a downstream consumer dogfooding a high-throughput import (~160k `JobRecord`s — 89k `Completed` / 68k `Failed` / 2.5k `Queued`; a 4-call "active jobs" dashboard timing out at ~30s). Re-derivation found the dashboard is the *mild* symptom of two defects, and that the underlying scenario — a large external source minting one job per row — is a granularity anti-pattern the engine should *survive* but the authoring surface should *prevent*. This addendum corrects the defects, hardens the engine against legitimate large backlogs, and makes **windowing** the Koan-native way to drain a large source.

### 19.1 Two defects (against the ratified design, not new questions)

**A — the composite index was ratified but never built.** §6 and decision §12.7 put a composite `(status, visibleAt)` index in the **v1 default** (not an opt-in knob). `JobRecord` shipped with **zero `[Index]`** → every ledger read is a COLLSCAN on stores that honor index metadata. Worse, the reads materialize-then-filter in memory: `DataJobLedger.Query` server-filters only `WorkType`, then `.Where`s status/workId in process; **`SelectCandidate` — the claim loop, every `PollInterval` — loads *all* `Queued` *and* *all* `Running` and sorts in memory**; `NonTerminal` does `All()`. The dashboard is on-demand; the claim loop is a per-second double-scan. This is an implementation miss against an accepted decision.

**B — retention has a hole.** Shipped archival (§16) purges only `Completed`/`Cancelled` older than `ArchiveAfter` (7d); **`Failed`/`Dead` are retained forever** — the consumer's ledger was ~43% `Failed`, unbounded by construction. `PurgeArchivable` itself materializes all benign-terminal rows to delete them (a COLLSCAN cleanup).

### 19.2 Why the query fixes don't, alone, save a bulk burst

A 1M-row import where **each row mints a job** costs end-to-end: 1M inserts + 1M claim-updates + 1M settle-updates (+ retries), each transition rewriting an append-only `Transitions` list and moving ~3 index entries — **~6–10M write operations**, dominated by per-row write amplification no index removes. Indexing keeps the hot path *queryable*; it does not make a per-row ledger a sane bulk pipe. Response is two-layered: **harden the engine** (19.3) and **change the authoring granularity** (19.4) — the latter is the real answer.

### 19.3 Engine hardenings (the four cliffs)

1. **Full sort-key index + pushed order+limit.** The claim order is `VisibleAt, FirstSubmittedAt`; in a burst `VisibleAt≈now` across the whole backlog, so `(Status, VisibleAt)` alone still returns the backlog to sort. Index **`(Status, VisibleAt, FirstSubmittedAt)`** and push `ORDER BY … LIMIT 1` (or `LIMIT batch`) into the store so "next" is an O(log n) seek. Add `(WorkType, Status)` (dashboard/coalesce) and `(WorkType, WorkId)` (the §17.2 exclusivity probe + history). Declarative `[Index]`; Reference = Intent for the store that honors it.
2. **Capability-graded batch claim.** Single-row optimistic claim makes N workers contend on the FIFO head (N−1 wasted CAS each); the `Ticket` window (1s) is incompatible with burst throughput. New `IJobLedger` capability **`AtomicBatchClaim`**: on a row-locking store, lease a chunk with `… FOR UPDATE SKIP LOCKED LIMIT K RETURNING` (Postgres/SqlServer) — zero contention, K rows per round-trip; Mongo falls back to a `findAndModify` loop; in-memory takes K under its lock. Election rides the existing capability ladder (§8) — adding the Postgres connector elects the better claim with no user code.
3. **TTL-index retention covering Failed/Dead + a count cap.** Prefer a **store-native TTL index** on `LastSettledAt` (Mongo TTL; PG scheduled delete / partition-drop) so expiry costs the engine nothing; extend coverage to **`Failed`/`Dead`** with a separate longer window (replayable until then) **and a per-`WorkType` count cap** (`keep last N`) so a burst is bounded by *count*, not just age. `ArchiveAfter` splits per-outcome (`CompletedAfter` / `FailedAfter` / `Cap`). The app-side batched purge remains the fallback where the store has no TTL.
4. **Sharded metrics, not a hot counter** — see 19.5.

### 19.4 The Koan-native answer: model the *window* as the work-item ("chunk, don't multiply")

The ledger is a **lifecycle/audit store** — durable claim, retry, cancellation, per-row transition history. That is worth paying for a *job*; it is pure overhead a million times over for homogeneous import rows. Entity-first already prescribes the fix: **the work-item is the unit of work — so make the unit a *window over the source*, not a row.**

**Primary pattern — the cursor-conveyor** (streaming / unknown size, bounded footprint). One window-job; on success it queues the *next* window via the existing `ctx.ContinueWith`. At most ~1 in flight; 1M rows drain as sequential windows, each terminal then swept — the ledger never holds a row per item, and it uses only primitives that already exist:

```csharp
public sealed class ImportWindow : Entity<ImportWindow>, IKoanJob<ImportWindow>
{
    public string Source { get; set; } = "";
    public long   Offset { get; set; }
    public int    Size   { get; set; } = 1000;

    public const string Pull = nameof(Pull);

    [JobAction(Pull)]                                     // serialized per (WorkType,WorkId) by default (§17.2)
    public static async Task Execute(ImportWindow w, JobContext ctx, CancellationToken ct)
    {
        var page = await ExternalSource.Page(w.Source, w.Offset, w.Size, ct);
        await page.Rows.Save();                           // one bulk upsert of the window
        ctx.Progress(page.Done ? 1 : (w.Offset + page.Count) / (double)page.Total);
        if (!page.Done)
        {
            w.Offset += page.Count;                       // advance the cursor on THIS entity (auto-saved §17.1)
            ctx.ContinueWith(Pull);                       // re-queue the same work-item at the next window
        }
    }
}

await new ImportWindow { Source = "vendor:catalog" }.Job.Submit(ImportWindow.Pull);   // kicks off ONE ledger row
```

> `ctx.ContinueWith(action)` re-queues the **same** work-item by action (not a new entity), so the conveyor is a
> self-loop on a mutable-offset entity — the cursor lives on the entity, conditional auto-save (§17.1) persists it,
> and per-entity serialization (§17.2) guarantees at most one window of a given conveyor runs at a time. Each window
> is a fresh ledger row (the `ContinueWith` follow-on); the *active* footprint is ~1, and the terminal rows are
> bounded by retention (19.3).

**Bounded fan** (throughput without explosion): start `P` conveyors over disjoint stripes (`offset ≡ stripe (mod P)`) → `P` in flight, `P` ledger rows at a time. **Pre-fan** (rows already in hand, not external): `rows.InWindowsOf(1000).Submit("process")` mints `total/1000` chunk-jobs rather than `total`.

**Guardrail (self-reporting).** The worker samples per-`WorkType` active counts and the boot/health report warns when one crosses a threshold — `[Koan.Jobs] WorkType 'ImportRow' active=512,000 — job-per-row smell; window the source (jobs-howto §bulk)`. The framework can't forbid a job-per-row design, but it will *name* it.

**Not now (YAGNI):** a built-in fan-out/map-reduce orchestration primitive. The conveyor + bounded-fan cover the dogfed cases with existing verbs; a first-class `Job.Fan(total, window, parallelism)` helper is sugar to add only if the manual stripe pattern recurs.

### 19.5 Metrics as an entity, sharded

`JobMetric : Entity<JobMetric>` keyed `(bucket, workType, outcome, shard)` carrying a `Count` (+ optional duration sums), incremented once per terminal settle into a random `shard ∈ [0,K)`; dashboards sum shards for a bucket. The shard fans concurrent increments across `K` rows (no single hot counter); the `bucket` (e.g. hour) bounds cardinality. This is what makes aggressive trimming **safe** — the counts survive in `JobMetric`, the rows don't. The "active jobs" panel reads `Queued`/`Running` counts from the now-indexed ledger; historical throughput reads `JobMetric`. On by default on the durable tier; off in inline test mode.

### Status / phasing

**Accepted 2026-06-10. Partially implemented (2026-06-10) — verified on real SQLite (the `Koan.Jobs.Adapter.Sqlite.Tests` suite, 48 green).** Each tier is its own failing-spec-first slice (ARCH-0079).

- **Tier 0 — DONE.** The `[Index]` triple `(Status,VisibleAt,FirstSubmittedAt)`/`(WorkType,Status)`/`(WorkType,WorkId)` + predicate/order/limit push-down in every `IJobLedger` read (the claim loop reads a bounded ordered window of `ClaimScanBatch`). `HighVolumeScanShapeSpec`: at 100k, the dashboard returns only matches <2s and the claim finds the FIFO head <1.5s. *(Honors §6/§12.7 — this was the implementation miss, not a new decision.)*
- **Data-layer (DONE, prerequisite).** SQLite `UpsertMany` was per-row autocommit (one fsync per row) → a bulk `list.Save()`/`AppendMany` of N rows cost N fsyncs. Wrapped in one transaction (Postgres/SqlServer already did; Mongo uses `BulkWrite`). 100k seed: minutes → ~1s.
- **Tier 1 — DONE.** `FailedAfter` window (closes the forever-retained Failed/Dead hole) + `RetainPerWorkType` count cap, both pushed down; `RetentionSpec` proves it. *(TTL-index variant — let the store expire `LastSettledAt` — is the remaining refinement; the app-side batched purge is the current mechanism.)*
- **§19.4 — DONE.** The cursor-conveyor pattern (built from existing `ctx.ContinueWith` + auto-save; `ConveyorSpec`: 5000 items → 50 ledger rows) and the job-per-row guardrail (`IJobLedger.CountActive` + a sweep warning over `JobPerRowWarnThreshold`).
- **Tier 2 — PENDING.** `JobMetric` worker-batched, node-sharded rollup + dashboard reads off it.
- **Tier 3 — PENDING.** `AtomicBatchClaim` (Postgres `SKIP LOCKED`) on the capability ladder.

The hot/cold partition **move** (§6, decision §12.7) stays the **opt-in** scale layout — windowing makes it unnecessary for the common bulk case, so it is not promoted to default here.

**Cross-tier validation (2026-06-10).** The §19 specs were folded into the shared `JobBehaviorSuite` (ARCH-0079) and run on every tier: **in-memory (44), SQLite (50), Postgres (40), SqlServer (40) — deterministically green**. The cross-tier run also corrected a real bug (Mongo enum lexicographic ordering in ledger predicates — equality sets now) and a test-fixture readiness-gating gap.

**`DrainAsync` chain-follow-on race (found + fixed 2026-06-10).** The Mongo tier intermittently flaked the chain/conveyor specs. Root cause: a race in `JobOrchestrator.DrainAsync` (§7). A chain successor is appended inside the predecessor's inflight task; the loop's `RemoveAll(completed); if (inflight.Count==0) break;` ran *before* awaiting inflight, so a successor appended in the window after `ClaimNext` returned null but before that `RemoveAll` was missed — the drain broke with a Queued, visible, unblocked successor and **nothing running**. In-memory/SQLite are too fast to hit the window; production's poll loop re-drains so it never surfaces there; a single `host.Drain()` on higher-latency Mongo exposes it. Fix: re-claim before breaking when a task just settled (`if (settled > 0) continue;`). This is correctness for `DrainAsync`'s contract on every store, not a test band-aid. Validated: Mongo ×8 green (was ~4/5 flaky) + all four other tiers still green. Full investigation: **docs/design/jobs-mongo-suite-flakiness-investigation.md**. (A first attempt — a §17.2 in-memory-exclusivity change — chased a phantom, regressed Mongo, and was reverted; the chain-claim/exclusivity is correct as-is.)

---

## 20. Addendum (2026-06-10): Scale-tier completion — metrics rollup (Tier 2), atomic claim (Tier 3), native TTL retention

**Status**: **Accepted + Implemented (2026-06-10)** — ratified by the architect. **Tier 2 metrics** (opt-in), **Tier 3 contention-free claim** (a generic conditional compare-and-set — refined from literal SKIP-LOCKED, see §20.3), and **TTL** (store-native on Mongo) are all shipped and green on all five tiers (§20.8 (a)/(b)/(c) DONE; (d) true SKIP-LOCKED distinct-rows deferred). Completes the three scale tiers §19 sketched but deferred (§19.5 metrics, §19.3 Tier 3 + TTL). Kept as a JOBS-0005 §20 addendum.
**Why now**: §19 shipped Tier 0 (index + pushdown) and Tier 1 (periodic retention) and proved the cross-tier suite green on all five stores. The three deferred items complete the high-throughput story, and each is **capability-graded** — a correct baseline on every store, a better primitive where the store offers one (the Koan capability ladder, ARCH-0084).

### 20.1 Scope correction — what Tier 0 already solved

Tier 0's pushed `(WorkType, Status)` index already turns the **active-count** dashboard ("how many Queued/Running?") into an O(matching-range) pushed `COUNT` (`CountActive`) — the original §19 30s timeout is **fixed**. So **Tier 2 is not about active counts.** The remaining gap is **outcome/throughput observability over time** (completed/failed per period, trend rates) that must **survive retention** — and you cannot count history from a ledger whose history Tier 1 deletes. That, and only that, requires accumulation.

### 20.2 Tier 2 — metrics as a worker-batched, node-sharded rollup

**[RATIFY] Decision**: accumulate in memory per node, flush periodically to a **per-node shard row** — *not* a per-settle DB write.
- Each orchestrator keeps an in-memory tally `(bucket, workType, outcome) → count`, bumped on terminal settle (Completed/Failed/Cancelled/Dead) — free.
- On a flush cadence (rides the existing archive sweep, or a dedicated `MetricsFlushInterval`), the node writes its accumulated deltas to **its own** shard row: `JobMetric : Entity<JobMetric>` keyed `(bucket, workType, outcome, nodeShard)`. Because **only that node writes its shard**, the read-add-write is contention-free — no cross-node lost updates, no dependence on an atomic `$inc` the data layer doesn't expose.
- Dashboard read: `JobMetric.Query(bucketRange, workType)` → group by outcome → `sum(Count)` — O(nodes × buckets × outcomes), independent of ledger size and immune to retention.
- Bucket granularity configurable (default hourly); metric rows are themselves retained/trimmed by bucket age (bounded).

**Alternatives rejected**:
- *Per-settle sharded `$inc`* (the §19.5 first sketch): one DB write per terminal job — 1M writes for a 1M-job burst — and needs a true atomic increment or risks lost updates. Worker-batched in-memory accumulation yields the identical rollup for ~O(flush) writes. Rejected on write-amplification.
- *Periodic recompute from the ledger*: cheap, but cannot see purged history; only serves active counts — already handled by Tier 0. Insufficient.

**[RATIFY] Enablement**: recommend **opt-in, default off** (`JobsOptions.Metrics.Enabled`, or Reference = Intent via a `Koan.Jobs.Metrics` marker). Rationale: keep the zero-config single-node path write-free; throughput dashboards are a deliberate choice. (Counter-argument for default-on: Koan's "self-reporting infrastructure" ethos — but the per-flush write on a tiny app is unwarranted. Architect's call.)
- *Node identity*: `_owner` is ephemeral per boot, so a restart orphans a shard row. Mitigated by bucket retention (old buckets purged → orphans age out). A stable node id is a future refinement.

### 20.3 Tier 3 — atomic, contention-free claim (capability-graded)

**Problem**: the default `Optimistic` claim is last-write-wins, **not a real CAS**. Under multi-node contention, N workers select the same FIFO head, all `Upsert` it `Running`, last writer wins, and **several workers execute the same job** — idempotency (the ratified contract) absorbs the duplicate *effect*, but the duplicate *executions* are wasted. `Ticket` avoids it but pays a ~1s election window. Both *cope* with contention; neither is contention-*free*.

**Decision (ratified; implementation refined the mechanism — 2026-06-10)**: add a generic data-layer **conditional compare-and-set** and have the durable claim mark `Running` through it, falling back to last-write-wins `Optimistic` where absent.
- **The primitive** — `IConditionalWriteRepository<TEntity,TKey>.ConditionalReplaceAsync(model, guard)` (Koan.Data.Abstractions), declared via **`DataCaps.Write.ConditionalReplace`**: "replace the stored row for `model.Id` **iff** it still matches `guard`." The guard is an ordinary `Expression<Func<TEntity,bool>>` lowered by the *existing* `LinqFilterCompiler` and each adapter's *existing* filter translator — no new SQL/dialect surface.
  - **Postgres / SqlServer / SQLite**: conditional `UPDATE … SET "Json" = … WHERE "Id" = @id AND <guard-as-json-path>` — 1 row affected = won, 0 = lost.
  - **Mongo**: `ReplaceOne(_id == id ∧ <guard>, model, upsert:false)` — a single-document atomic replace, **no transaction** (so it works on single-node Mongo); `ModifiedCount > 0` = won.
  - `RepositoryFacade` forwards the interface to the inner adapter (probed by the capability token).
- **The claim** — `DataJobLedger` keeps `SelectCandidates` (the §17.2-settled lane/gate/exclusive filter, returning the runnable batch in FIFO order) and marks the head via the CAS with guard `Status==Queued ∧ Owner==null`; on a CAS-loss it **falls through to the next runnable candidate**. This **preserves exclusivity unchanged** and is **single-execution** — two claimers can't both win a row.

**Why CAS-by-id, not literal `SELECT … FOR UPDATE SKIP LOCKED`-the-head**: empirically (verified in impl), Koan stores every entity as a JSON document (`Id` + `Json`), and Mongo's single-doc `findOneAndUpdate` can't express the cross-row **exclusivity** invariant (no subquery). Skip-locked-the-head therefore wouldn't compose with §17.2 on Mongo without a racy claim-then-revert dance — the exact §17.2 hazard. The conditional CAS composes on **every** adapter (incl. single-node Mongo), keeps the settled exclusivity, and delivers the contention *correctness* (no double-execution). It is also broadly reusable (optimistic concurrency for any entity), per the no-stopgaps rule.

**Phasing**: **(a) atomic single-claim — DONE.** The contention fix; the `DrainAsync` loop is unchanged (still one-at-a-time; the ledger iterates its runnable batch internally). **(d) true SKIP-LOCKED distinct-rows** — a throughput optimization that hands each worker a *different* row in one round-trip (relational subquery + Mongo claim-then-check) — remains deferred; the CAS already removes the wasted executions, so (d) is measured-need only.

**Scope note**: claim contention is a **multi-node competing-consumers** problem. A single worker draining a 1M backlog is the only claimer — Tier 0 + the cursor-conveyor (§19.4) already cover that. Tier 3 is the distributed-tier rung of the ladder.

**Validation**: a cross-tier `concurrent_claimers_take_distinct_jobs_no_double_claim` spec (8 claimers, 24 jobs) — each job claimed exactly once on all five tiers (in-memory/SQLite via lock/single-writer; PG/SqlServer/Mongo via the CAS).

### 20.4 TTL — store-native expiry where supported (capability-graded)

**[RATIFY] Decision**: add an absolute `ExpireAt` to `JobRecord`, computed at settle per outcome (`Completed/Cancelled → LastSettledAt + ArchiveAfter`; `Failed/Dead → LastSettledAt + FailedAfter`). On TTL-capable stores (Mongo) declare a TTL index on `ExpireAt` (`expireAfterSeconds = 0`) so the store expires rows continuously at **zero app cost**. The Tier 1 periodic purge **remains** the universal mechanism and the backstop (non-TTL stores; Mongo's ~60s TTL granularity; clock skew).

**Why a computed `ExpireAt` (not TTL on `LastSettledAt`)**: a single TTL index can't express per-outcome windows (Completed 7d vs Failed 30d) off one timestamp. Folding the per-outcome window into an absolute `ExpireAt` gives both windows with one index. Non-terminal rows leave `ExpireAt` null → naturally excluded.

**Alternatives rejected**: PG partition-drop — heavier, needs a partition primitive; the periodic purge is sufficient there. Mongo is the only store that earns the native-TTL upgrade today.

### 20.5 Data-layer capability additions (cross-pillar, landed here as first consumer)

These are general-purpose Koan.Data additions, dogfooded by jobs but documented for reuse:
- **`DataCaps.Write.ConditionalReplace`** + **`IConditionalWriteRepository<TEntity,TKey>`** (DONE) — declared/implemented by SQLite, Postgres, SqlServer, Mongo; forwarded by `RepositoryFacade`. A reusable optimistic-concurrency / compare-and-set primitive (the guard reuses the adapter's existing filter translator, so no new SQL surface). Drives the Tier 3 claim.
- **`DataCaps.Retention.TtlIndex`** + **`[Index(Ttl = true)]`** on `IndexAttribute` → Mongo `CreateIndexModel` with `CreateIndexOptions.ExpireAfter = TimeSpan.Zero` on the absolute-expiry field (TTL phase, pending). No-op on adapters lacking the capability.

### 20.6 What does NOT change
- The **at-least-once + idempotent** contract is unchanged on every tier — Tier 3 reduces *duplicate executions* under contention (efficiency) but does not promise exactly-once.
- The ledger remains the single source of truth; `JobMetric` is a derived, lossy-tolerant rollup (a drift-correcting full recount is a future option, not required).
- All five tiers stay green at every step (ARCH-0079 cross-tier specs), gated by the full-green ratchet.

### 20.7 Consequences
**Positive**: cheap throughput/trend dashboards that survive retention (Tier 2); contention-free, single-execution claiming for competing consumers (Tier 3); continuous zero-overhead expiry on Mongo at scale (TTL); two reusable data-layer capabilities (`SkipLocked`, `TtlIndex`) + `[Index(Ttl)]`.
**Negative**: a new `JobMetric` entity + a flush path (opt-in); per-adapter claim code behind a capability (more surface, but fallback-guarded); a new `ExpireAt` field + `[Index]` extension.
**Neutral**: SQLite/InMemory unchanged (claim + retention); PG/SqlServer keep periodic purge; the default zero-config path is unchanged unless metrics/claim-upgrades are present.

### 20.8 Phasing ledger (green at every step; ARCH-0079 cross-tier specs)
- **(a) Tier 2 metrics (opt-in) — ✅ DONE (d40b602e).** `JobMetric` entity + in-memory accumulator + periodic flush + `JobMetric.Summary` read API + cross-tier spec (counts survive a retention purge). Green on all 5 tiers.
- **(b) Tier 3 atomic single claim — ✅ DONE.** `DataCaps.Write.ConditionalReplace` + `IConditionalWriteRepository` on SQLite/PG/SqlServer/Mongo + `RepositoryFacade` forwarding + `Data<T,K>.As<>` + `DataJobLedger` runnable-batch CAS with optimistic fallback + the `concurrent_claimers_…_no_double_claim` cross-tier spec. Green on all 5 tiers (in-memory 46, SQLite 52, PG 42, SqlServer 42, Mongo 46).
- **(c) TTL — ✅ DONE.** `[Index(Ttl=true)]` (carried on `IndexSpec`/`IndexMetadata`) + `DataCaps.Retention.TtlIndex` + Mongo `CreateIndexOptions.ExpireAfter = 0` wiring + the relational adapters **skip** TTL indexes (`ProjectionResolver` + `RelationalModelBuilder`, no redundant index on a high-write table) + `JobRecord.ExpireAt` set per-outcome at settle (Completed/Cancelled → `ArchiveAfter`, Failed/Dead → `FailedAfter`). Specs: cross-tier `a_settled_job_gets_an_absolute_expiry` + a Mongo `index_ttl_materializes_a_mongo_ttl_index`. Green on all 5 tiers (in-memory 47, SQLite 53, PG 43, SqlServer 43, Mongo 48). The actual row deletion is Mongo's background TTL monitor (~60s); the Tier 1 periodic purge stays the universal mechanism and the backstop.
- **(d) Tier 3 batch / true SKIP-LOCKED distinct-rows — DEFERRED.** Optional throughput follow-on; ship only if measurements justify it (the CAS already removes double-execution).

**ADR placement**: kept as a JOBS-0005 **§20 addendum** (consistent with §17/§18/§19). Trivial to extract if the architect prefers a standalone record.
