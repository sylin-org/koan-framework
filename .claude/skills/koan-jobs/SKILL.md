---
name: koan-jobs
description: Entity-first background jobs, IKoanJob<TSelf>, .Job/.Jobs accessors, the ledger-is-the-queue capability ladder, JobContext steering
---

# Koan Jobs

## Trigger this skill when you see

- `IKoanJob<T>` / a `static Execute(MyJob job, JobContext ctx, CancellationToken ct)` handler
- `.Job.Submit(...)` / `.Jobs.Trigger(...)` / `.Job.Status()` accessors, or `list.Submit(...)`
- `[JobAction]`, `[JobChain]`, `[JobIdempotent]`, `[JobGate]`, `[JobPersistence]`, `[ParallelSafe]`
- `JobContext` verbs — `ctx.Progress`, `ctx.ContinueWith`, `ctx.StopChain`, `ctx.Reschedule`, `ctx.Backoff`
- References to `Koan.Jobs` / `Koan.Jobs.Transport.Messaging`
- "background job", "queue", "worker", "scheduled task", "retry", "at-least-once", "lane", "conveyor"
- Talk of running work off the request thread, draining a large feed, or cron/`@boot`/`@continuous` cadences

## Core principle

**The ledger is the queue.** A job is an `Entity<T>` that implements `IKoanJob<T>` and provides one `static Execute`. The type is auto-discovered (`[KoanDiscoverable]`), so there are **no** queues, workers, or repositories to wire — adding `Koan.Jobs` and writing the entity is the whole 90% case (Reference = Intent).

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

// Submit from anywhere — a controller, a service, a startup task:
await new ThumbnailJob { SourceUrl = url }.Job.Submit();
var status = await thumb.Job.Status();                         // latest JobStatus for this work-item
```

Koan persists the entity, claims the job on a worker, runs `Execute`, and — if you changed it — saves the reference it handed you. When `Execute` returns, the job is **Completed**.

## The capability ladder (Reference = Intent)

The same handler code runs on every tier — the contract is constant **at-least-once + idempotent**. You move up the ladder by adding references and infrastructure, never by rewriting jobs ([JOBS-0005](../../docs/decisions/JOBS-0005-job-orchestrator-rebuild.md)).

| Add this | Effect |
|---|---|
| `Koan.Jobs` (alone) | In-memory tier — ephemeral, single-process. Great for dev and fire-and-forget. |
| `+ any data adapter` | **Durable** tier — the ledger (`Entity<JobRecord>`) follows your adapter; claim/retry/history survive restart. |
| `+ multiple nodes on the shared ledger` | **Distributed** — competing consumers; the claim is an atomic compare-and-set, so each ready job runs on exactly one node. |
| `+ Koan.Jobs.Transport.Messaging` | Cross-node **push-dispatch** over `Koan.Messaging` — no claim-latency polling. |

Lane-fair dispatch keeps a busy lane (e.g. crawl) from starving a quiet downstream one (e.g. translation); each non-empty lane gets a guaranteed share ([JOBS-0008](../../docs/decisions/JOBS-0008-lane-fair-dispatch.md)).

## Attributes you'll declare

| Attribute | What it does |
|---|---|
| `[JobAction("name", Timeout=…, MaxAttempts=…, OnFailure=…, Lane=…, MaxConcurrency=…, Schedule=…)]` | Per-action policy: per-attempt timeout, retry cap, concurrency lane, and a level-trigger (`Schedule` = interval / cron / `@boot` / `@continuous`). |
| `[JobChain("a", "b", "c")]` | Linear pipeline; on a stage's success the orchestrator persists the work-item and auto-advances to the next. |
| `[JobIdempotent("Key1", "Key2")]` | Declares the coalesce key — re-delivery is deduped and concurrent duplicates collapse into one run. |
| `[JobGate("PropertyName")]` | Names the work-item property (or an async resolver method) whose value forms a shared-resource gate, checked at dispatch *before* the handler runs. |
| `[JobPersistence(JobPersistenceMode.DataStore)]` | Pins the durability tier (`Auto` / `InMemory` / `DataStore`) for this work-type, overriding the capability election. |

`[ParallelSafe]` opts a type out of the default per-instance serialization (see write-safety below).

## Write-safety: mutate the entity you're given

Koan loads the work-item fresh, passes it to `Execute`, and persists *that* reference **only if you changed it** (untouched → nothing is written). Do your work on the `job` parameter — **don't reload a second copy and save it yourself**, or your write and Koan's `save-if-changed` will race. A handler that genuinely owns its own persistence simply leaves the passed entity untouched, and Koan writes nothing.

A work-item id is its **ordering key**: jobs for the same `(WorkType, WorkId)` run **one at a time** (FIFO) by default. Different entities parallelize fully; assert `[ParallelSafe]` only when same-id runs are provably independent.

## Steering a run: the `JobContext`

Everything a handler needs arrives on `ctx`. When success / throw isn't enough, call **at most one** control verb per execution:

```csharp
public static async Task Execute(Crawl job, JobContext ctx, CancellationToken ct)
{
    if (await RateLimited(job.Source))
        ctx.Backoff(TimeSpan.FromMinutes(2), key: job.Source); // gate the shared resource; defers peers too
    else if (job.More)
        ctx.ContinueWith("next-page");                          // branch / extend the chain
    await ctx.Progress(0.5, "halfway");                          // durable progress → dashboards
}
```

| Member | What it gives you |
|---|---|
| `ctx.Services` | a scoped `IServiceProvider` for DI inside the handler |
| `ctx.Cancellation` | the token to honor (also the per-action timeout) |
| `ctx.Action` / `ctx.Logger` | the stage being executed (empty for single-action jobs) / a logger for this job |
| `ctx.State` | read-only history: `Attempt`, `Reschedules`, `FirstSubmittedAt`, `LastError`, … |
| `ctx.Progress(fraction, msg)` | report durable progress to the ledger |
| `ctx.ContinueWith(action)` / `ctx.StopChain()` | steer a declared chain |
| `ctx.Reschedule(after \| until)` / `ctx.Backoff(after, key?)` | cooperative backoff — defers **without** consuming a retry (a level-trigger, not a failure) |

## Draining a large source: window, don't multiply

`list.Submit(action)` mints one job *per item* — right for hundreds or a few thousand, **wrong** for "import a million rows". The ledger is a lifecycle/audit store; minting a million rows means a million writes and a saturated active set. The fix is a coarser **unit of work**: make the **window** the job, not the row.

```csharp
public sealed class ImportWindow : Entity<ImportWindow>, IKoanJob<ImportWindow>
{
    public const string Pull = nameof(Pull);
    public string Source { get; set; } = "";
    public long   Offset { get; set; }
    public int    Size   { get; set; } = 1000;

    [JobAction(Pull)]                                    // serialized per work-item by default
    public static async Task Execute(ImportWindow w, JobContext ctx, CancellationToken ct)
    {
        var page = await ExternalSource.Page(w.Source, w.Offset, w.Size, ct);
        await page.Rows.Save();                          // one bulk upsert of the window
        if (!page.Done)
        {
            w.Offset += page.Count;                      // advance the cursor on THIS entity (auto-saved)
            ctx.ContinueWith(ImportWindow.Pull);         // re-queue the same work-item at the next window
        }
    }
}

await new ImportWindow { Source = "vendor:catalog" }.Job.Submit(ImportWindow.Pull);   // ONE ledger row
```

At most one window is in flight, so the source drains through a handful of ledger rows instead of a million. Want parallelism without the explosion? Start a few conveyors over disjoint stripes (`Offset ≡ stripe (mod P)`). Mint a job per row at scale and the worker logs a **job-per-row warning** (`JobPerRowWarnThreshold`) pointing right back here.

## Operate & tune

```csharp
// Status / cancel / type-level trigger
await myJob.Job.Status();      await MyJob.Jobs.Status(id);     await MyJob.Jobs.WithStatus(JobStatus.Running);
await myJob.Job.Cancel();      await MyJob.Jobs.Cancel(id);     await MyJob.Jobs.Trigger(action);   // schedule's on-demand twin

// Tune — AddKoanJobs(o => …) (registrar-driven; never hand-register the worker)
o.ArchiveAfter; o.FailedAfter; o.RetainPerWorkType;   // retention windows + per-type cap
o.ClaimStrategy; o.ClaimScanBatch;                    // claim contention strategy + bounded per-lane seek
o.LaneWeights["translation"] = 3;                     // lane-fair dispatch weight (default 1 = equal share)
o.QueueAgeWarning = TimeSpan.FromMinutes(5);          // oldest-queued-age tripwire → /health Degraded
```

## Anti-patterns to flag

| If you see | Suggest |
|---|---|
| A hand-rolled `IHostedService` + `Channel<T>` / `BlockingCollection` worker loop | `IKoanJob<T>` — the ledger is the queue; claim/retry/dispatch are framework concerns. |
| Injecting `IJobQueue` / a repository to enqueue work | `myJob.Job.Submit()` / `MyJob.Jobs.Trigger(action)` — the accessors are the surface. |
| Reloading the work-item inside `Execute` and calling `.Save()` yourself | Mutate the `job` parameter; Koan `save-if-changed`'s it. Your own save races the orchestrator. |
| `list.Submit()` over a huge/external feed (a job per row) | A **conveyor** — make the window the job (`ctx.ContinueWith` self-loop). |
| Manual `Task.Delay` + re-submit to retry/throttle | `ctx.Reschedule(after)` / `ctx.Backoff(after, key)` — defers without burning a retry. |
| A bespoke cron `Timer` / `BackgroundService` for cadence | `[JobAction(Schedule="…")]` — interval / cron / `@boot` / `@continuous`. |
| Registering the jobs worker in `Program.cs` | Reference `Koan.Jobs`; the registrar wires everything (Reference = Intent). |

## See also

- [Background Jobs how-to](../../docs/guides/jobs-howto.md) — the authoritative walkthrough (durability tiers, testing, full `JobContext`)
- [Reference card: jobs.md](../../docs/reference/cards/jobs.md) — one-screen pillar map
- [JOBS-0005 — job orchestrator rebuild](../../docs/decisions/JOBS-0005-job-orchestrator-rebuild.md) — the ledger model + capability ladder
- [JOBS-0008 — lane-fair dispatch](../../docs/decisions/JOBS-0008-lane-fair-dispatch.md) — fairness across lanes
