# Koan Jobs

Reference `Sylin.Koan.Jobs` when an Entity-owned business transition should run outside the request
that requested it, survive retries, and remain inspectable.

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

The package registers its coordinator, worker, ledger, health contributor, and default in-process
wake transport through `AddKoan()` discovery. Application registration code is not required.

## Capability ladder

| Composed infrastructure | Elected behavior |
|---|---|
| No durable data adapter | In-memory ledger; work is lost on restart |
| SQLite, Postgres, SQL Server, Mongo, or another durable adapter | Data-backed ledger; job state survives restart |
| Shared durable store across nodes | Competing consumers share the ledger |
| `Sylin.Koan.Jobs.Transport.Messaging` | Cross-node wake signal; the ledger remains the source of truth |

Inspect `jobs:ledger` and `jobs:transport` through `/.well-known/Koan/facts` or `koan://facts`.
The standard `/health/ready` response includes queue depth, running depth, reclaim backlog, and oldest
queued age in Development; production returns only aggregate readiness. Per-work-item status and history
are available through `entity.Job` and `Entity.Jobs`.

## Boundaries

- Execution is at-least-once; handlers must be idempotent.
- The in-memory tier is development/test convenience, not durability.
- Durable SQLite behavior does not prove every distributed provider or topology.
- A custom MCP or HTTP action that submits work remains responsible for its business authorization.
- Use a window or batch as the job for large sources; do not create an unbounded job per input row.

Use the [Jobs pillar reference](../../docs/reference/cards/jobs.md) for the compact API map and the
[Jobs guide](../../docs/guides/jobs-howto.md) for scheduling, retries, chains, gates, and testing.
