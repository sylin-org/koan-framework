---
type: REF
domain: jobs
title: "Jobs — pillar map"
audience: [developers, ai-agents]
status: current
last_updated: 2026-07-18
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-18
  status: verified
  scope: docs/reference/cards/jobs.md
---

# Jobs — pillar map

> One-screen map of the Jobs pillar — entity-first background work, ledger-is-the-queue. Full detail: [jobs-howto.md](../../guides/jobs-howto.md).

**What it does** — Durable background jobs authored as entities. A work-type is `MyJob : Entity<MyJob>, IKoanJob<MyJob>` with one `static Execute(...)` handler; the type is auto-discovered (`[KoanDiscoverable]`) so there are **no** queues, workers, or repositories to wire. The ledger *is* the queue, and a capability ladder upgrades it by package reference — in-memory by default, durable once a data adapter is present, distributed competing-consumers on the shared ledger, and cross-node wake hints through a directly elected Communication connector that claims framework signals ([JOBS-0005](../../decisions/JOBS-0005-job-orchestrator-rebuild.md), [ARCH-0113](../../decisions/ARCH-0113-entity-capability-communication.md)). The same handler code runs on every tier (at-least-once + idempotent), and lane-fair dispatch keeps a busy lane from starving a quiet one ([JOBS-0008](../../decisions/JOBS-0008-lane-fair-dispatch.md)).

## The one canonical pattern

Derive the work-item from `Entity<T>`, implement `IKoanJob<T>`, and write one `static Execute`. Submit via the `.Job` (instance) / `.Jobs` (static) accessors.

```csharp
public sealed class SendDigest : Entity<SendDigest>, IKoanJob<SendDigest>
{
    public string UserId { get; set; } = "";

    public static async Task Execute(SendDigest job, JobContext ctx, CancellationToken ct)
    {
        var mail = ctx.Services.GetRequiredService<IMailer>();
        await mail.SendDigestAsync(job.UserId, ct);   // mutate `job` to record state — the orchestrator save-if-changed's it
    }
}

var job = new SendDigest { UserId = "u-123" };
await job.Job.Submit();                                // enqueue this work-item (single-action job)
await job.Job.Submit("retry", after: TimeSpan.FromMinutes(5)); // deferred action
var status = await job.Job.Status();                   // latest JobStatus for this work-item
```

Trigger a scheduled/type-level job with `SendDigest.Jobs.Trigger("action")`. Submit a finite selection
or provider-bounded stream with the same pointwise intent:

```csharp
JobSubmission selected = await digests.Where(digest => digest.Ready).Submit();
JobSubmission streamed = await SendDigest.QueryStream(digest => digest.Ready).Submit();
```

The fixed-size summary reports confirmed ledger acceptance (`Submitted + Coalesced`), not execution.
Typed failure and cancellation exceptions preserve the accepted prefix; source submission is ordered,
one-pass, sequentially backpressured, and not collection-atomic. Streaming bounds producer memory,
not ledger growth—model a window/conveyor as the job for very large sources.

## Policy attributes

| Attribute | What it does |
|---|---|
| `[JobAction("name", Timeout=…, MaxAttempts=…, Lane=…, Schedule=…)]` | Per-action policy: per-attempt timeout, retry cap, concurrency lane, and a level-trigger (`Schedule` = interval / cron / `@boot` / `@continuous`). |
| `[JobChain("a", "b", "c")]` | Linear pipeline; on a step's success the orchestrator persists the work-item and auto-advances to the next stage. |
| `[JobIdempotent("Key1", "Key2")]` | Declares the coalesce key; re-delivery is deduped and concurrent duplicates collapse into one run. |
| `[JobGate("PropertyName")]` | Names the work-item property whose value forms a shared-resource gate, checked at dispatch *before* the handler runs. |
| `[JobPool("pool-name")]` | Defers member election to one standard-DI `IJobPoolResolver` at claim time for a live resource pool. |
| `[JobPersistence(JobPersistenceMode.DataStore)]` | Requires durable Data-backed execution for this work type; host composition rejects if no durable provider can honor it. |
| `[ParallelSafe]` | Opts out of the default per-instance serialization when actions on one Entity are proven independent. |

These are optional business/operational decisions. The single-action path requires none of them.

## The escape hatch

When success / throw isn't enough, the handler steers its own run through `JobContext` control verbs — call at most one per execution:

```csharp
public static async Task Execute(Crawl job, JobContext ctx, CancellationToken ct)
{
    if (await RateLimited(job.Source))
        ctx.Backoff(TimeSpan.FromMinutes(2), key: job.Source); // gate the shared resource; defer peers too
    else if (job.More)
        ctx.ContinueWith("next-page");                          // branch the chain
    await ctx.Progress(0.5, "halfway");                          // durable progress to the ledger
}
```

`Reschedule(after|until)` defers the same stage without consuming a retry; `StopChain()` ends a declared chain early; `Backoff` and `Reschedule` are level-triggers, not failures. Read-only `ctx.State` carries the orchestration snapshot for stateful decisions.

## The sample that shows it

[`samples/GoldenJourney`](../../../samples/GoldenJourney/README.md) —
`ReviewRequest : Entity<ReviewRequest>, IKoanJob<ReviewRequest>` performs a business assessment,
records durable progress, and exposes the result through the same HTTP and agent-visible application.
