namespace Koan.Jobs;

/// <summary>
/// The single source of truth and the single writer (JOBS-0005 §7). The ledger <em>is</em> the queue: dispatch
/// claims the next ready row by atomic CAS; there is no separate volatile queue to reconcile. Physical layout
/// (in-memory, data-backed, hot/cold partitions) hides behind this interface; the orchestrator is storage-agnostic.
/// </summary>
public interface IJobLedger
{
    /// <summary>Append a new job (Submit). For the durable tier this participates in the ambient transaction (outbox).</summary>
    Task Append(JobRecord record, CancellationToken ct);

    /// <summary>Append a batch in one shot.</summary>
    Task AppendMany(IReadOnlyCollection<JobRecord> records, CancellationToken ct);

    Task<JobRecord?> Get(string jobId, CancellationToken ct);

    /// <summary>Find a non-terminal job with the given coalesce key (idempotency / concurrent-duplicate collapse).</summary>
    Task<JobRecord?> FindActiveByCoalesceKey(string workType, string coalesceKey, CancellationToken ct);

    /// <summary>
    /// Atomically claim the next ready job: <c>Status==Queued &amp;&amp; VisibleAt&lt;=now &amp;&amp; CancelRequestedAt==null</c>,
    /// whose lane is not in <paramref name="saturatedLanes"/> and whose <c>GateKey</c> is not under an active gate;
    /// CAS to <see cref="JobStatus.Running"/>, stamping <paramref name="owner"/> + <paramref name="leaseUntil"/>.
    /// Returns null if nothing is claimable. This is the hot path and must be atomic under contention.
    /// </summary>
    Task<JobRecord?> ClaimNext(string owner, DateTimeOffset now, DateTimeOffset leaseUntil,
        IReadOnlyCollection<string> saturatedLanes, CancellationToken ct);

    /// <summary>Persist a transition (settle / advance / defer / cancel). The orchestrator is the only caller — single writer.</summary>
    Task Update(JobRecord record, CancellationToken ct);

    /// <summary>Update only durable progress for an in-flight job (cheap, off the transition path).</summary>
    Task Progress(string jobId, double fraction, string? message, CancellationToken ct);

    /// <summary>Running jobs whose lease lapsed (the reaper sweep).</summary>
    Task<IReadOnlyList<JobRecord>> Stuck(DateTimeOffset now, CancellationToken ct);

    /// <summary>All non-terminal jobs (the boot-recovery sweep).</summary>
    Task<IReadOnlyList<JobRecord>> NonTerminal(CancellationToken ct);

    /// <summary>Queued jobs of a type resting in a given action/stage (the level-triggered reconcile sweep).</summary>
    Task<IReadOnlyList<JobRecord>> InStage(string workType, string action, CancellationToken ct);

    /// <summary>Facade/dashboard query.</summary>
    Task<IReadOnlyList<JobRecord>> Query(JobQuery query, CancellationToken ct);

    // --- shared resource gates (cooperative backoff) ---
    Task SetGate(string gateKey, DateTimeOffset releaseAt, string? reason, CancellationToken ct);
    Task<IReadOnlyList<JobGate>> ActiveGates(DateTimeOffset now, CancellationToken ct);

    /// <summary>Remove benign terminal rows (Completed/Cancelled) settled before <paramref name="olderThan"/>, keeping
    /// the active set lean. Returns the number purged.</summary>
    Task<int> PurgeArchivable(DateTimeOffset olderThan, CancellationToken ct);

    /// <summary>Remove Failed/Dead rows settled before <paramref name="olderThan"/> (replayable until then) — the §19.3
    /// completion of retention so failures don't accumulate forever. Returns the number purged.</summary>
    Task<int> PurgeFailed(DateTimeOffset olderThan, CancellationToken ct);

    /// <summary>Trim terminal rows for a work-type to the newest <paramref name="keep"/>, removing the older terminal
    /// rows (the per-work-type count cap, §19.3). Returns the number removed.</summary>
    Task<int> TrimTerminal(string workType, int keep, CancellationToken ct);

    /// <summary>Count active (non-terminal) rows for a work-type — the cheap pushed probe behind the §19.4
    /// job-per-row guardrail.</summary>
    Task<long> CountActive(string workType, CancellationToken ct);
}
