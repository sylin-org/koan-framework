---
type: GUIDE
domain: jobs
title: "Background Jobs How-To"
audience: [developers, architects]
status: current
last_updated: 2026-06-04
framework_version: v0.6.3
validation:
  date_last_tested: 2026-06-04
  status: verified
  scope: In-memory and SQLite tiers
related_guides:
  - data-modeling.md
  - building-apis.md
  - performance.md
---

# Koan Jobs: Your Complete Guide

This guide walks you through Koan's background jobs, from your first `myJob.Job.Submit()` to durable, distributed pipelines that survive restarts and ride out rate limits. Think of it as a conversation with a colleague who's run a lot of background work in production—we'll start with a one-line job and build up to chains, schedules, and cooperative backoff.

Each section follows a gentle rhythm: **Concept** (what is this?), **Recipe** (how do I set it up?), **Sample** (show me the code), and **When to use it**. By the end you'll know how to model any background workload as a plain entity and let the orchestrator handle the rest.

**The one idea to hold onto:** a job is just an **entity that knows how to do work**. You write the entity and a single `Execute` method; Koan does the queuing, claiming, retrying, scheduling, and cancelling. You never see a queue, a worker, or a coordinator.

**Related Guides:**
- Modeling the entity itself? → [Data Modeling](data-modeling.md)
- Kicking jobs off from an API? → [Building APIs](building-apis.md)
- Throughput and tuning? → [Performance](performance.md)

---

## 0. Prerequisites

Add the Jobs package alongside the Koan baseline:

```xml
<PackageReference Include="Koan.Core" Version="0.6.3" />
<PackageReference Include="Koan.Data.Core" Version="0.6.3" />
<PackageReference Include="Koan.Jobs" Version="0.6.3" />
```

That's all the wiring there is. **Reference = Intent**: adding `Koan.Jobs` and implementing the job interface is enough—`AddKoan()` discovers your jobs and starts the orchestrator automatically.

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();   // discovers your jobs, starts the worker
var app = builder.Build();
app.Run();
```

With no data adapter referenced, jobs run in-memory (fast, ephemeral). Add a data adapter (SQLite, Postgres, Mongo, SQL Server) and the same jobs become **durable**—they survive restarts. You change nothing in your job code; see [§10](#10-durability-pick-your-tier).

---

## 1. Your first job

**Concept.** A job is an `Entity<T>` that implements `IKoanJob<T>` and provides one static `Execute` method. The entity carries the data; `Execute` does the work.

**Recipe.** Declare the entity, implement `Execute`, call `.Job.Submit()`.

**Sample.**

```csharp
using Koan.Data.Core.Model;
using Koan.Jobs;

public sealed class ThumbnailJob : Entity<ThumbnailJob>, IKoanJob<ThumbnailJob>
{
    public string SourceUrl { get; set; } = "";
    public string? ThumbUrl { get; set; }

    public static async Task Execute(ThumbnailJob job, JobContext ctx, CancellationToken ct)
    {
        job.ThumbUrl = await Thumbs.Make(job.SourceUrl, ct);   // do the work, mutate the entity
    }
}
```

Kick it off from anywhere—a controller, a service, a startup task:

```csharp
await new ThumbnailJob { SourceUrl = url }.Job.Submit();
```

The job runs in the background. Koan persists the entity, claims the job on a worker, runs `Execute`, and—if you changed it—saves the entity it handed you. When `Execute` returns, the job is **Completed**.

> **Mutate the entity you're given.** Koan loads the work-item fresh, passes it to `Execute`, and persists *that* reference if you changed it (untouched → nothing is written). So do your work on the `job` parameter—don't reload a second copy and save it yourself, or your write and Koan's will race. Need an escape hatch? A handler that genuinely owns its own persistence simply leaves the passed entity untouched; Koan then writes nothing.

**Check on it later** by the entity's id:

```csharp
JobStatus? status = await thumb.Job.Status();          // this instance
JobStatus? status = await ThumbnailJob.Jobs.Status(id); // by id, from anywhere
```

**When to use it.** Anything you don't want to block a request on: image processing, sending mail, calling a slow API, generating a report.

---

## 2. One model, many kinds of work

**Concept.** A model often needs several *kinds* of work done to it. A `PresetPackage` might be **Fetched**, **Parsed**, **Minted**, then **Published**. Each kind is an **action**. The same `Execute` handles them all and switches on `ctx.Action`.

**Recipe.** Define your action tokens as constants, submit with an action, switch on `ctx.Action`.

**Sample.**

```csharp
public static class Stage     // typed tokens — symbol-checked, stored by name
{
    public const string Fetch   = nameof(Fetch);
    public const string Parse   = nameof(Parse);
    public const string Publish = nameof(Publish);
}

public sealed class PresetPackage : Entity<PresetPackage>, IKoanJob<PresetPackage>
{
    public string Source { get; set; } = "";
    public string? Raw { get; set; }
    public Preset? Parsed { get; set; }

    public static Task Execute(PresetPackage pkg, JobContext ctx, CancellationToken ct) => ctx.Action switch
    {
        Stage.Fetch   => Fetch(pkg, ct),
        Stage.Parse   => Parse(pkg, ct),
        Stage.Publish => Publish(pkg, ctx.Services.GetRequiredService<IPublisher>(), ct),
        _             => Task.CompletedTask,
    };
}
```

```csharp
await pkg.Job.Submit(Stage.Fetch);   // run one action
```

A single-action job (§1) simply ignores `ctx.Action`. Need a service inside the handler? Reach for `ctx.Services`—it's a scoped provider.

**When to use it.** Any aggregate that flows through stages, or any model that supports several discrete operations.

---

## 3. Pipelines: chaining actions

**Concept.** When actions run in sequence, declare a `[JobChain]`. On each step's success Koan persists the (mutated) entity and **auto-advances** to the next stage. The entity *is* the state that flows down the pipeline.

**Recipe.** Add `[JobChain]` listing the stages in order. Submit the first stage; the rest follow.

**Sample.**

```csharp
[JobChain(Stage.Fetch, Stage.Parse, Stage.Mint, Stage.Publish)]
public sealed class PresetPackage : Entity<PresetPackage>, IKoanJob<PresetPackage>
{
    // Fetch sets pkg.Raw → Parse reads pkg.Raw, sets pkg.Parsed → Mint → Publish
}
```

```csharp
await pkg.Job.Submit(Stage.Fetch);   // runs Fetch → Parse → Mint → Publish
```

**Steer the chain from inside a handler:**

```csharp
ctx.StopChain();                 // finish here, don't advance
ctx.ContinueWith(Stage.Publish); // branch: go to a specific stage next instead of the default
```

Prefer to drive it yourself? Skip `[JobChain]` and enqueue the next step explicitly:

```csharp
case Stage.Fetch:
    await Fetch(pkg, ct);
    await pkg.Job.Submit(Stage.Parse);   // manual hand-off — branch freely
    break;
```

**When to use it.** Multi-step processing where each step depends on the previous one. Keep chains **linear**—for branching or parallel fan-out, drive the steps yourself.

---

## 4. Policy: retries, timeouts, lanes

**Concept.** Each action gets its own policy via `[JobAction]`—how many times to retry, how long it may run, how many can run at once, and what a failure means for the chain. Unspecified knobs inherit sensible defaults.

**Recipe.** Decorate the model with one `[JobAction]` per action that needs tuning.

**Sample.**

```csharp
[JobAction(Stage.Fetch,   Timeout = "00:10:00", MaxAttempts = 5, OnFailure = OnFailure.Abort,
                          Lane = "upstream", MaxConcurrency = 4)]
[JobAction(Stage.Publish, Timeout = "00:00:30", MaxAttempts = 1)]
public sealed class PresetPackage : Entity<PresetPackage>, IKoanJob<PresetPackage> { … }
```

- **`Timeout`** — the handler is cancelled (cooperatively, via `ct`) if it runs too long.
- **`MaxAttempts`** — failed runs retry with exponential backoff; once exhausted the job is **Failed**.
- **`OnFailure`** — `Abort` (default) stops a chain on failure; `Continue` proceeds to the next stage anyway.
- **`Lane` / `MaxConcurrency`** — a lane is a concurrency pool. By default each action is its own lane, so a slow `Fetch` never starves `Publish`. Set `Lane` to share a pool, `MaxConcurrency` to cap it.

**One entity, one job at a time.** Independently of lanes, Koan serializes jobs by work-item id: two different actions on the *same* entity (say `FetchPreview` and `DriftCheck` on one `Package`) never run concurrently—the entity is processed in order, like a FIFO group keyed by its id. Different entities still run fully in parallel. If a type's actions are genuinely independent and you want them to overlap on one instance, opt out with `[ParallelSafe]` on the class—an assertion that they don't conflict.

**When to use it.** Whenever an action talks to something rate-limited, slow, or fragile—tune its retries, timeout, and concurrency independently of the others.

---

## 5. Idempotency and coalescing

**Concept.** Background work is delivered **at-least-once**, so handlers should be safe to run twice. `[JobIdempotent]` makes that ergonomic: declare the properties that identify "the same work," and Koan collapses duplicate submissions into one.

**Recipe.** Add `[JobIdempotent]` with the key properties.

**Sample.**

```csharp
[JobIdempotent(nameof(Source), nameof(Version))]
public sealed class PresetPackage : Entity<PresetPackage>, IKoanJob<PresetPackage>
{
    public string Source { get; set; } = "";
    public int Version { get; set; }
}
```

Submit the same `(Source, Version, action)` twice and only one job runs—the second submit coalesces onto the first.

**When to use it.** Any job that could be submitted more than once for the same logical unit (a webhook retried by the sender, a user double-clicking, a sweep re-enqueuing).

---

## 6. Cooperative backoff and resource gates

**Concept.** Sometimes a job *can't* make progress right now—an upstream returned `429 Too Many Requests` with a cooldown, or a resource is briefly locked. That's **not a failure**, so it shouldn't burn a retry. Instead the handler asks to be **rescheduled**, and—optionally—sets a shared **gate** so its peers back off too.

**Recipe.** Call `ctx.Reschedule(...)` to defer this job; `ctx.Backoff(...)` to defer *and* gate the whole resource. Declare the resource with `[JobGate]` so the orchestrator can skip gated jobs **before** running them.

**Sample.**

```csharp
[JobGate(nameof(Host))]   // the resource these jobs contend for
public sealed class FetchJob : Entity<FetchJob>, IKoanJob<FetchJob>
{
    public string Host { get; set; } = "";

    public static async Task Execute(FetchJob job, JobContext ctx, CancellationToken ct)
    {
        var res = await Http.Get(job.Host, ct);
        if (res.StatusCode == 429)
        {
            // back off this host for everyone; honor Retry-After, escalate if it keeps happening
            if (ctx.State.Reschedules >= 3) ctx.Reschedule(Tomorrow9am());
            else ctx.Backoff(res.RetryAfter ?? TimeSpan.FromMinutes(5));
            return;
        }
        // …success…
    }
}
```

Once one job hits the 429 and calls `Backoff`, every other job for the same `Host` is deferred **at dispatch—without running**—until the cooldown passes. `ctx.State` gives the handler its own history (`Reschedules`, `Attempt`, `FirstSubmittedAt`, …) so it can escalate from "+5 minutes" to "tomorrow morning."

**When the gate identity isn't a property on the entity.** `[JobGate]` can name a **method** instead of a property — an async resolver that composes the key from a related entity, a registry, or anything in DI, evaluated once at submit:

```csharp
[JobGate(nameof(ResolveHostGate))]
public sealed class Mirror : Entity<Mirror>, IKoanJob<Mirror>
{
    public string PackageId { get; set; } = "";

    // the host lives on the navigated Package, not on Mirror — so resolve it
    public async Task<string?> ResolveHostGate(IServiceProvider sp, CancellationToken ct)
    {
        var pkg = await Package.Get(PackageId, ct);
        return pkg is null ? null : $"host:{new Uri(pkg.UpstreamArchive).Host}";
    }
}
```

The resolved key is frozen onto the job exactly like a property gate, so dispatch matching is unchanged and `ctx.Backoff()` (no argument) reuses it — one host's 503 defers only the siblings that resolved to the *same* host. No synthetic `Host` column on the entity. (The resolver runs at submit, so the gate identity must be knowable then — true for a host or upstream endpoint; a value chosen only at call time is out of scope.)

- `ctx.Reschedule(TimeSpan after)` / `ctx.Reschedule(DateTimeOffset until)` — defer this job; **no retry consumed**.
- `ctx.Backoff(TimeSpan after, key?)` — defer **and** gate the resource so peers wait too.

**When to use it.** Anything that calls a rate-limited or occasionally-unavailable dependency. The gate turns a thundering herd of retries into one polite wait.

---

## 7. Scheduling: run work on a cadence

**Concept.** Besides kicking a job off directly (edge-triggered), an action can be **scheduled**. On the cadence you declare, Koan submits a fresh job for that action and runs it the normal way—claim, execute, settle. It's the reconcile-loop model: edge for latency, schedule for "make sure this keeps happening."

**Recipe.** Add a `Schedule` to the action's `[JobAction]`.

**Sample.**

```csharp
[JobAction("Reconcile",   Schedule = "00:10:00")]   // every 10 minutes (a TimeSpan interval)
[JobAction("NightlySweep", Schedule = "0 2 * * *")] // cron — daily at 02:00 UTC
[JobAction("Warm",         Schedule = "@boot")]      // once at startup
[JobAction("Pump",         Schedule = "@continuous")]// every scheduler tick
public sealed class Maintenance : Entity<Maintenance>, IKoanJob<Maintenance> { … }
```

`Schedule` accepts a **TimeSpan interval** (`"00:10:00"`), a **cron** expression (`"0 2 * * *"`), or a **sentinel** (`@boot`, `@continuous`). A scheduled action runs on its own **singleton** work-item, so its handler typically does the bulk operation or fans out per-entity submits:

```csharp
public static async Task Execute(Maintenance _, JobContext ctx, CancellationToken ct)
{
    foreach (var pkg in await Package.Query(p => p.NeedsRefresh, ct))
        await pkg.Job.Submit("MirrorRefresh");   // the tick fans out edge jobs; the orchestrator stays domain-ignorant
}
```

Pair a scheduled action with `[JobIdempotent]` (a stable key) so an overlapping tick **coalesces** onto the one still in flight instead of piling up.

**When to use it.** Periodic reconciliation, retries of a whole class of work, "every night at…" maintenance. It also makes the system self-healing: edge submits give low latency, the schedule guarantees the work eventually happens. To run a scheduled action **on demand**, see `Trigger` in §8.

---

## 8. Batches and the type-level facade

**Concept.** `model.Job.X` operates on one instance; `MyModel.Jobs.X` is the **whole job subsystem** for the type—batch submit, trigger a type-level action, query, cancel by id.

**Recipe.** Use the instance accessor for one item, the static facade for many (or for no instance at all).

**Sample.**

```csharp
// one instance
await pkg.Job.Submit(Stage.Fetch);

// a thousand instances, one bulk enqueue
List<PresetPackage> packages = …;
await packages.Submit(Stage.Fetch);

// run a type-level action now, with no instance (the on-demand twin of a schedule)
await PresetPackage.Jobs.Trigger(Stage.Discover);

// the type's job subsystem
await PresetPackage.Jobs.Cancel(id);
var running = await PresetPackage.Jobs.WithStatus(JobStatus.Running);
var mine    = await PresetPackage.Jobs.Query(new JobQuery(Action: Stage.Fetch));
```

**`Trigger` vs `Submit`.** `instance.Job.Submit(action)` runs the action for *that* instance; `MyModel.Jobs.Trigger(action)` runs it at the **type level** against an auto-provisioned singleton—the manual counterpart to a `Schedule`. Both return a `JobHandle`, so you can `await (await …).Completion(timeout)` to run-and-wait.

**When to use it.** `list.Submit(action)` for fan-out; `Jobs.Trigger(action)` for "run the nightly sweep now" admin actions; `MyModel.Jobs` for dashboards and operating on jobs by id.

---

## 9. Cancellation

**Concept.** Cancellation is durable and cross-process. A queued job is cancelled before it runs; a running job is asked to stop cooperatively through its `CancellationToken`.

**Sample.**

```csharp
await pkg.Job.Cancel();                  // this instance
await PresetPackage.Jobs.Cancel(id);     // by id, from any node
```

Honor it in long-running handlers by passing `ct` to the calls you `await`. A cancelled job ends in the **Cancelled** state.

**When to use it.** User-initiated "stop," superseded work, draining before shutdown.

---

## 10. Durability: pick your tier

**Concept.** The same job code runs across tiers; the infrastructure you reference decides durability and scale—never correctness. The delivery contract (at-least-once, idempotent) is constant everywhere.

| You have… | Jobs are… |
|---|---|
| no data adapter | in-memory, fast, lost on restart |
| a data adapter (SQLite/Postgres/Mongo/SQL Server) | **durable**—survive restarts |
| several nodes on the same store | **distributed**—nodes share the work |

**Per-type control.** Override the tier for one model with `[JobPersistence]`:

```csharp
[JobPersistence(JobPersistenceMode.InMemory)]   // keep this high-churn job ephemeral
public sealed class PingJob : Entity<PingJob>, IKoanJob<PingJob> { … }
```

`Auto` (default) follows your adapters; `InMemory` keeps a job's queue state volatile even when a store is present (the orchestration is ephemeral—handy for a torrent of fire-and-forget work you don't want touching the database); `DataStore` insists on durability. Mixed tiers coexist in one app: durable and in-memory jobs run side by side, and a cooperative gate (§6) set by one is honored by the other—so a 429 backoff still protects a shared host across tiers.

**Claim strategy.** When several nodes compete for the same job, choose how they settle it in `JobsOptions`:

```csharp
builder.Services.AddKoanJobs(o => o.ClaimStrategy = ClaimStrategy.Ticket);
```

`Optimistic` (default) is cheapest and relies on idempotent handlers; `Ticket` runs a leaderless GUIDv7 election (each contender drops a ticket, the earliest wins) to sharply cut duplicate runs across nodes. Both work on every adapter.

**When to use which.** Single service → defaults. Multiple replicas pulling the same queue → `Ticket`. A torrent of fire-and-forget pings you don't want cluttering the store → `[JobPersistence(InMemory)]`.

**Push dispatch (lower latency).** Out of the box a worker wakes the instant *it* submits a job and otherwise polls at `PollInterval`. Reference **`Koan.Jobs.Transport.Messaging`** and a submit on *any* node fans a lightweight "job ready" wake across the bus, so every node claims new work immediately instead of waiting out its poll interval. It's purely a latency upgrade—the ledger is still the truth, so a dropped signal costs at most one poll interval and never correctness.

**Transactional submit (outbox).** On the durable tier, a `Submit` inside an ambient transaction is part of that transaction—the job is enqueued **on commit** and **discarded on rollback**. So a job submitted as a side effect of saving an entity can never be "saved but never enqueued," and a rolled-back save never leaves a stray job:

```csharp
using (EntityContext.Transaction("publish"))
{
    await order.Save();
    await order.Job.Submit(Stage.Notify);   // enqueued only if the transaction commits
    await EntityContext.Commit();
}
```

No configuration—it's automatic whenever a transaction is in scope.

**Retention.** Completed and Cancelled jobs older than `ArchiveAfter` (default 7 days) are swept out automatically so the active ledger stays lean. Failed and Dead jobs are **kept**—they're queryable and replayable. Tune `ArchiveAfter` / `ArchiveInterval`, or set `ArchiveAfter` to zero to disable.

---

## 11. Testing your jobs

**Concept.** Jobs are testable without containers or background timers. **Inline mode** runs a job synchronously on submit, so a test reads like a function call.

**Sample.**

```csharp
services.AddKoanJobs(o => o.Mode = JobMode.Inline);

await new ThumbnailJob { SourceUrl = url }.Job.Submit();   // runs now, synchronously
var saved = await ThumbnailJob.Get(id);
saved!.ThumbUrl.Should().NotBeNull();
```

For schedules, timeouts, and deferrals, inject a `TimeProvider` (Koan uses the standard `System.TimeProvider`) and a fake clock to **advance time** instead of waiting—no flakiness, no real delays.

**When to use it.** Always—assert your handler's logic and chain decisions in milliseconds.

---

## 12. The `JobContext` at a glance

Everything a handler needs arrives on `ctx`:

| Member | What it gives you |
|---|---|
| `ctx.Action` | the stage being executed (empty for single-action jobs) |
| `ctx.Services` | a scoped `IServiceProvider` for DI inside the handler |
| `ctx.Cancellation` | the token to honor (also the per-action timeout) |
| `ctx.Logger` | a logger for this job |
| `ctx.State` | read-only history: `Attempt`, `Reschedules`, `FirstSubmittedAt`, `LastError`, … |
| `ctx.Progress(0.5, "…")` | report durable progress (surfaced to dashboards) |
| `ctx.ContinueWith(action)` / `ctx.StopChain()` | steer a chain |
| `ctx.Reschedule(after \| until)` / `ctx.Backoff(after, key?)` | cooperative backoff |

---

## 13. Quick reference

```csharp
// Author
public sealed class MyJob : Entity<MyJob>, IKoanJob<MyJob>
{
    public static Task Execute(MyJob job, JobContext ctx, CancellationToken ct) => …;
}

// Attributes
[JobChain(A, B, C)]                                  // linear pipeline
[JobAction(A, Timeout="…", MaxAttempts=…, OnFailure=…, Lane="…", MaxConcurrency=…, Schedule="…")]
[JobIdempotent(nameof(Key))]                          // dedupe + coalesce
[JobGate(nameof(Host))]                               // shared backoff key — a property, or an async resolver method
[JobPersistence(JobPersistenceMode.InMemory)]         // per-type durability
[ParallelSafe]                                        // opt out of per-entity serialization

// Submit & operate
await myJob.Job.Submit();                 await myJob.Job.Submit(action);
await myJob.Job.Submit(action, after);    await list.Submit(action);
await myJob.Job.Cancel();                 await MyJob.Jobs.Cancel(id);
await myJob.Job.Status();                 await MyJob.Jobs.WithStatus(JobStatus.Running);
await MyJob.Jobs.Trigger(action);         // type-level action, no instance (schedule's on-demand twin)

// From a handler
ctx.Progress(0.4, "…");  ctx.ContinueWith(next);  ctx.StopChain();
ctx.Reschedule(5.Minutes());  ctx.Backoff(retryAfter);  // (TimeSpan helpers are illustrative)
```

That's the whole surface. Write the entity, write `Execute`, and let Koan run it—reliably, on whatever infrastructure you've got.
