---
type: REFERENCE
domain: jobs
title: "Run retryable background work"
audience: [developers, operators, architects, ai-agents]
status: current
last_updated: 2026-07-22
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-22
  status: verified
  scope: Entity-first jobs, submission, persistence ladder, retry, scheduling, and inspection
---

# Run retryable background work

Use Koan Jobs when work must survive the request that created it, retry after failure, run later, or
run on a schedule. The work item is an Entity and its ledger is the queue; application code does not
declare a queue, worker, repository, or discovery attribute.

## Smallest useful job

Reference `Sylin.Koan.Jobs`, keep `AddKoan()`, and implement one discovered work type:

```csharp
public sealed class SendDigest : Entity<SendDigest>, IKoanJob<SendDigest>
{
    public string AccountId { get; set; } = "";

    public static async Task Execute(SendDigest job, JobContext context, CancellationToken ct)
    {
        var mailer = context.Services.GetRequiredService<IMailer>();
        await mailer.SendDigest(job.AccountId, ct);
    }
}

var job = new SendDigest { AccountId = account.Id };
await job.Job.Submit();
var status = await job.Job.Status();
```

`IKoanJob<T>` carries discovery. The in-memory runtime is the local floor; an eligible Data connector
adds a durable ledger, and a qualifying Communication connector can add cross-node wake hints. The
handler stays the same across those tiers.

## Add policy only when needed

| Need | Expression |
|---|---|
| Named action, retry cap, timeout, lane, or schedule | `[JobAction(...)]` |
| Linear multi-stage work | `[JobChain("first", "second")]` |
| Coalesce equivalent submissions | `[JobIdempotent(...)]` |
| Serialize work sharing a scarce resource | `[JobGate(...)]` or `[JobPool(...)]` |
| Require durable execution | `[JobPersistence(JobPersistenceMode.DataStore)]` |
| Permit independent actions on one instance | `[ParallelSafe]` |

Jobs are at-least-once. Make externally visible effects idempotent even when a coalesce key is used.
Durable mode rejects at composition when no referenced provider can honor it.

## Submit one, many, or later

```csharp
await job.Job.Submit("retry", after: TimeSpan.FromMinutes(5));
await SendDigest.Jobs.Trigger("daily");

JobSubmission selected = await digests.Where(item => item.Ready).Submit();
JobSubmission streamed = await SendDigest.QueryStream(item => item.Ready).Submit();
```

A collection submission reports confirmed ledger acceptance, not execution. It is ordered,
sequentially backpressured, and not collection-atomic. Streaming bounds producer memory, not ledger
growth; model an explicit work window for very large sources.

## Steer a running attempt

`JobContext` can report progress, reschedule, back off a shared resource, continue a chain, or stop a
chain. Use at most one terminal control decision per execution. Throwing is a failed attempt; a normal
return is success.

```csharp
if (await IsRateLimited(job.AccountId, ct))
    context.Backoff(TimeSpan.FromMinutes(2), key: job.AccountId);
else
    await context.Progress(0.5, "halfway");
```

## Inspect and correct

- Query status/progress from the job accessor and ledger.
- Read startup and runtime facts for persistence tier, schedules, lanes, and connector participation.
- Use `/health/ready` for selected durable dependencies.
- Treat retries, timeouts, cancellation, and redelivery as observable outcomes, not hidden worker logs.
- If a required durability or signal capability is absent, add the qualifying package/service or
  relax the job's declared requirement; do not register a parallel scheduler.

The [background jobs guide](../../guides/jobs-howto.md) owns the deeper recipes for chains, schedules,
gates, pools, cooperative control, and operational tuning.
