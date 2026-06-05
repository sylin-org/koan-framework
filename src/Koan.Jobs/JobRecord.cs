using Koan.Data.Core.Model;

namespace Koan.Jobs;

/// <summary>
/// The enqueued unit of work and the single source of truth — one ledger entry = (work-item × action × lifecycle).
/// Persisted as <c>Entity&lt;JobRecord&gt;</c> so the durable tier rides the existing data layer (no per-DB job
/// adapters; durability follows the ambient adapter). The <c>Id</c> is the Job id; <see cref="WorkId"/>
/// points at the work-item entity. JOBS-0005 §3/§7/§9.
/// </summary>
public sealed class JobRecord : Entity<JobRecord>
{
    /// <summary>Stable key of the discovered <see cref="IKoanJob"/> work-item type (its full name).</summary>
    public string WorkType { get; set; } = "";

    /// <summary>Id of the work-item entity this job acts on.</summary>
    public string WorkId { get; set; } = "";

    /// <summary>The action/stage being executed (empty for single-action jobs).</summary>
    public string Action { get; set; } = "";

    public JobStatus Status { get; set; } = JobStatus.Created;

    /// <summary>Failure-and-retry count (distinct from <see cref="Reschedules"/>).</summary>
    public int Attempt { get; set; }

    /// <summary>Cooperative-deferral count (Reschedule != Retry).</summary>
    public int Reschedules { get; set; }

    /// <summary>Ready to claim when <c>VisibleAt &lt;= now</c> (future for delayed/deferred jobs).</summary>
    public DateTimeOffset VisibleAt { get; set; }

    public DateTimeOffset FirstSubmittedAt { get; set; }
    public DateTimeOffset? LastSettledAt { get; set; }

    /// <summary>Concurrency lane (defaults to the action name).</summary>
    public string Lane { get; set; } = "";

    // --- lease (claim ownership) ---
    public string? Owner { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }

    // --- coalesce / idempotency ---
    public string? CoalesceKey { get; set; }

    // --- gate (cooperative backoff) ---
    public string? GateKey { get; set; }

    /// <summary>When true (default), this job holds its work-item exclusively: no other job for the same
    /// <c>(WorkType, WorkId)</c> may run concurrently. False for <c>[ParallelSafe]</c> types (JOBS-0005 §17.2).</summary>
    public bool Exclusive { get; set; } = true;

    // --- cancellation (durable marker) ---
    public DateTimeOffset? CancelRequestedAt { get; set; }

    // --- diagnostics / observability ---
    public string? LastError { get; set; }
    public string? DeferReason { get; set; }
    public DateTimeOffset? Deadline { get; set; }
    public string? CorrelationId { get; set; }
    public double ProgressFraction { get; set; }
    public string? ProgressMessage { get; set; }
    public string? DeadReason { get; set; }

    /// <summary>Append-only audit trail of status transitions.</summary>
    public List<JobTransition> Transitions { get; set; } = new();

    /// <summary>True when the job is in a terminal state.</summary>
    public bool IsTerminal => Status is JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled or JobStatus.Dead;

    /// <summary>Deep copy. The in-memory ledger stores/returns clones so it converges with the durable tier
    /// (which round-trips through the store), keeping the single-writer-via-Update contract identical.</summary>
    public JobRecord Clone()
    {
        var c = (JobRecord)MemberwiseClone();
        c.Transitions = Transitions.ConvertAll(t => new JobTransition { At = t.At, From = t.From, To = t.To, Note = t.Note });
        return c;
    }
}
