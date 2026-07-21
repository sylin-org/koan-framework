namespace Koan.Jobs;

/// <summary>
/// Lifecycle status of a <see cref="JobRecord"/> — a Job being one enqueued action on a work-item.
/// JOBS-0005 §9. A <em>deferred</em> job (reschedule/backoff/gate) stays <see cref="Queued"/> with a
/// future <c>VisibleAt</c> and a <c>DeferReason</c>; it is not a distinct status.
/// </summary>
public enum JobStatus
{
    /// <summary>Constructed, not yet appended to the ledger.</summary>
    Created = 0,
    /// <summary>On the ledger, claimable once <c>VisibleAt &lt;= now</c> (includes deferred jobs).</summary>
    Queued = 1,
    /// <summary>Claimed by a worker (lease held), handler executing.</summary>
    Running = 2,
    /// <summary>Terminal — handler succeeded (and no further chain stage).</summary>
    Completed = 4,
    /// <summary>Terminal — handler failed after exhausting retries (chain may continue per OnFailure).</summary>
    Failed = 5,
    /// <summary>Terminal — a durable cancel marker was honored.</summary>
    Cancelled = 6,
    /// <summary>Terminal — poison (retries exhausted with no continue) or perpetually-deferred (Deadline/MaxReschedules).</summary>
    Dead = 7,
}
