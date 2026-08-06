# Sylin.Koan.Jobs

Reference `Sylin.Koan.Jobs` when an Entity-owned business transition should run outside the request
that requested it, survive retries, and remain inspectable.

## Install

```powershell
dotnet add package Sylin.Koan.Jobs
```

The package composes through the application's existing `AddKoan()` call. No Jobs-specific registration is
required. With no durable Data provider, the automatic ledger is deliberately in-memory; reference SQLite,
PostgreSQL, SQL Server, MongoDB, or another durable Koan Data provider when work must survive restart.

## Usage: run Entity-owned work

```csharp
public sealed class ReviewRequest : Entity<ReviewRequest>, IKoanJob<ReviewRequest>
{
    public ReviewPriority Priority { get; set; }

    public static Task Execute(ReviewRequest request, JobContext context, CancellationToken ct)
    {
        request.Assess();
        return context.Progress(1, "Ready for review");
    }
}

await review.Job.Submit();
var status = await review.Job.Status();
```

The same pointwise intent applies to a business selection or a provider-bounded Entity stream:

```csharp
JobSubmission selected = await reviews
    .Where(review => review.Priority == ReviewPriority.High)
    .Submit();

JobSubmission streamed = await ReviewRequest
    .QueryStream(review => review.Priority == ReviewPriority.High)
    .Submit();
```

`JobSubmission` is a fixed-size summary of ledger acceptance, not handler completion. `Accepted`
counts new ledger records plus declared-idempotency coalesces; `PendingCommit` says those records are
still contingent on the ambient transaction. A source or submission failure throws
`JobSubmissionException`, and cancellation throws `JobSubmissionCanceledException`; both preserve the
confirmed accepted prefix without retaining per-item handles.

The package registers its coordinator, worker, ledger, health contributor, and Communication-backed
wake hint through `AddKoan()` discovery. Application registration code is not required.

Submission also captures every composed `IKoanContextCarrier` before its first asynchronous boundary.
Execution restores that opaque context before loading the work item; an absent registered axis is
explicitly suppressed. Unknown axes or invalid carrier data dead-letter the job before application
handler code. Tenant and subject values therefore survive a durable hop without Jobs naming either
concept or requiring application plumbing.

## Capability ladder

| Composed infrastructure | Elected behavior |
|---|---|
| No durable data adapter | In-memory ledger; work is lost on restart |
| SQLite, Postgres, SQL Server, Mongo, or another durable adapter | Data-backed ledger; job state survives restart |
| Shared durable store across nodes | Competing consumers share the ledger |
| Direct Communication connector that claims framework signals, such as RabbitMQ | The same internal wake hint crosses nodes; the ledger remains the source of truth |

Inspect `jobs:ledger`, `jobs:wake`, and `communication:framework-signals:default` through
`/.well-known/Koan/facts` or `koan://facts`.
The standard `/health/ready` response includes queue depth, running depth, reclaim backlog, and oldest
queued age in Development; production returns only aggregate readiness. Per-work-item status and history
are available through `entity.Job` and `Entity.Jobs`.

Optional retained throughput is one operation surface rather than a framework Entity. Enable
`JobsOptions.MetricsEnabled`, then read outcome totals that survive ledger retention:

```csharp
var outcomes = await JobMetrics.Summary(
    typeof(ReviewRequest).FullName!,
    DateTimeOffset.UtcNow.AddDays(-1),
    DateTimeOffset.UtcNow);
```

## Boundaries

- Execution is at-least-once; handlers must be idempotent.
- The in-memory tier is development/test convenience, not durability.
- `[JobPersistence(JobPersistenceMode.DataStore)]` fails host composition when no durable Data adapter is available;
  Koan never silently weakens that declared requirement to in-memory execution.
- Durable SQLite behavior does not prove every distributed provider or topology.
- Source submission is pointwise, sequential, and not collection-atomic. A provider call that throws
  is not reported as confirmed acceptance; retry that item under the job's declared idempotency policy.
- A custom MCP or HTTP action that submits work remains responsible for its business authorization.
- Metrics are derived, opt-in, and lossy-tolerant; the Jobs ledger remains the source of truth.
- Use a window or batch as the job for large sources; do not create an unbounded job per input row.

Use the [Jobs reference](../../docs/reference/jobs/index.md) for the greenfield API map and the
[Jobs guide](../../docs/guides/jobs-howto.md) for scheduling, retries, chains, gates, and testing.
For deterministic tests against the production engine, reference
[`Sylin.Koan.Jobs.Testing`](../Koan.Jobs.Testing/README.md).
