---
type: RECIPE
recipe: run-work-in-background
title: "Return quickly and finish the work reliably"
domain: jobs
status: current
last_updated: 2026-08-19
audience: [ai-agents, developers]
framework_version: v1.0.0
validation:
  status: not-yet-tested
  scope: docs/recipes/run-work-in-background.md
gets_you: "Slow work moved off the request, surviving restarts, with progress and failure you can see."
works_if: "Something takes long enough that a caller should not wait for it."
costs: "Adds no service on the local path. Adds a second thing to operate and observe."
ingredients:
  - "one | durable and scheduled work | Sylin.Koan.Jobs"
  - "optional | deterministic Job tests | Sylin.Koan.Jobs.Testing"
---

# Return quickly and finish the work reliably

A Job is an Entity that owns its own execution, so the receipt and the work are the same record.

## When this is the answer

"Uploading is slow." "Send the email after." "Run this every night." "Import this file."

The distinction that matters: **accepting work and completing work are different contracts.** Say this
out loud, because it changes the API. The caller gets a receipt immediately; the receipt is what they
poll, subscribe to, or show progress from. An application that returns "done" before the work is done
is lying, and an application that makes the caller wait has not solved the problem.

Ask:

- **Can it run twice?** It will. Retries and duplicate submissions happen, so the handler must be
  idempotent or must detect the duplicate. Deciding this after the fact means reconciling bad data.
- **What should the user see while it runs?** Nothing, a status they poll, or live progress.
- **What happens when it fails permanently?** Silent failure is the common defect — someone must be
  able to see a dead job.
- **Scheduled or triggered?** Both are jobs; only one needs a clock.

## Assembly

```powershell
dotnet add package Sylin.Koan.Jobs
```

```csharp
public sealed class Review : Entity<Review>, IKoanJob<Review>
{
    public static Task Execute(Review review, JobContext context, CancellationToken ct) => ...;
}
```

The Entity is the receipt: its fields carry status and result, and the same record is what HTTP
returns and what the handler updates. No parallel job table.

One Entity can own several named actions, each with its own timeout and retry budget, dispatched on
`ctx.Action`. Report progress into the ledger so a caller can watch it:

```csharp
[JobAction(Ingest, Timeout = "00:15:00", MaxAttempts = 3)]
[JobAction(Reanalyze, Timeout = "00:10:00", MaxAttempts = 3)]
public sealed class PhotoProcessingJob : Entity<PhotoProcessingJob>, IKoanJob<PhotoProcessingJob>
{
    public static async Task Execute(PhotoProcessingJob job, JobContext ctx, CancellationToken ct)
    {
        switch (ctx.Action)
        {
            case Ingest:
                await service.Process(job, (fraction, stage) => ctx.Progress(fraction, stage), ct);
                break;
        }
    }
}
```

When a job consumes staged input, **delete the staging only after success** — a retry must be able to
reread the original bytes. Cleaning up in a `finally` is the version that loses work.

Depth: [jobs how-to](../guides/jobs-howto.md).

## Prove it

1. **Behavior** — submit, get a receipt immediately, and assert the work completes and the receipt
   reflects it.
2. **Composition** — assert the job ledger you intended is the one in use.
3. **Correction** — assert retry, duplicate submission, cancellation, and permanent failure each behave
   as promised, and that a failed job is *visible* rather than merely absent.

Restart the host mid-flight if durability is part of the claim. Durability nobody restarted is a hope.

## Boundaries

- A job is not a transaction across external systems. Partial completion is possible; design for it.
- It does not make a non-idempotent handler safe to retry.
- Scheduling is not a guarantee of exactly-once execution.

## Interacts with

**Tenancy.** Work that leaves the request thread must carry the ambient tenant, or it reads nothing and
appears to succeed.

**AI.** Embedding, generation, and other model work belongs here rather than on the request, and this
is where the ambient-context problem shows up first.

**Human review.** A job that produces something a person must approve pairs with
[review before it ships](review-ai-output.md).
