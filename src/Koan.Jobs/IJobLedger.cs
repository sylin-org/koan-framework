namespace Koan.Jobs;

/// <summary>
/// The single source of truth and the single writer (JOBS-0005 §7). The ledger <em>is</em> the queue: dispatch
/// claims the next ready row directly; there is no separate volatile queue to reconcile. Data-backed implementations
/// use provider CAS when available and retain an explicit optimistic at-least-once fallback otherwise. Physical layout
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
    /// for pool jobs, elects a free member from <paramref name="pools"/> and stamps it as <c>GateKey</c>;
    /// CAS to <see cref="JobStatus.Running"/>, stamping <paramref name="owner"/> + <paramref name="leaseUntil"/>.
    /// Returns null if nothing is claimable. This is the hot path; implementations use their strongest available
    /// concurrency primitive while preserving the documented at-least-once floor.
    /// </summary>
    Task<JobRecord?> ClaimNext(string owner, DateTimeOffset now, DateTimeOffset leaseUntil,
        IReadOnlyCollection<string> saturatedLanes, CancellationToken ct,
        IReadOnlyDictionary<string, PoolDispatchContext>? pools = null);

    /// <summary>Persist a transition (settle / advance / defer / cancel). The orchestrator is the only caller — single writer.</summary>
    Task Update(JobRecord record, CancellationToken ct);

    /// <summary>
    /// Guarded lease renewal (JOBS-0009): push <paramref name="leaseUntil"/> forward only while the row is
    /// <see cref="JobStatus.Running"/> and owned by <paramref name="owner"/>. Returns false when another
    /// claimant owns the row (the caller abandons — it never settles a row it no longer owns). A narrow
    /// purpose-built mid-flight write, same family as <see cref="Progress"/>; never a full-record replace.
    /// </summary>
    Task<bool> TryRenewLease(string jobId, string owner, DateTimeOffset leaseUntil, DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// Ownership-guarded settlement (PMC-055 fencing): apply <paramref name="record"/>'s terminal or
    /// re-queued state only while the stored row is still <see cref="JobStatus.Running"/> and owned by
    /// <paramref name="expectedOwner"/>. Returns false when the claim was lost (another node reclaimed the
    /// row) — the caller abandons without writing, so a revived zombie cannot clobber the new claimant.
    /// </summary>
    Task<bool> TrySettle(JobRecord record, string expectedOwner, CancellationToken ct);

    // --- reservation dispatch (PMC-056) ---

    /// <summary>
    /// Guarded assignment (PMC-056): stamp <c>ReservedFor=<paramref name="hand"/></c> on a Queued row only while it is
    /// still Queued and unreserved. The assignment is ledger-verifiable dispatch metadata (no side queues, no routing
    /// state): the row stays Queued and writes no transition. Capability-graded like <see cref="TrySettle"/>;
    /// a false return means another coordinator won the stamp or the row moved on.
    /// </summary>
    Task<bool> TryReserve(string jobId, string hand, DateTimeOffset reservedUntil, DateTimeOffset now, CancellationToken ct);

    /// <summary>The oldest due, unreserved, non-cancelled Queued rows in dispatch order — what an active
    /// coordinator considers for assignment, bounded by <paramref name="limit"/> so a deep backlog costs one
    /// indexed seek, never a scan.</summary>
    Task<IReadOnlyList<JobRecord>> ReservationCandidates(DateTimeOffset now, int limit, CancellationToken ct);

    /// <summary>All currently-reserved non-terminal rows regardless of hand or age — bounded by outstanding
    /// assignments (fleet capacity), never by backlog size. The coordinator sweeps this for lapses and dead hands.</summary>
    Task<IReadOnlyList<JobRecord>> Reservations(CancellationToken ct);

    /// <summary>Update only durable progress for an in-flight job (cheap, off the transition path).</summary>
    Task Progress(string jobId, double fraction, string? message, CancellationToken ct);

    /// <summary>Running jobs whose lease lapsed (the reaper sweep).</summary>
    Task<IReadOnlyList<JobRecord>> Stuck(DateTimeOffset now, CancellationToken ct);

    /// <summary>All currently-Running rows (PMC-055 death sweep: the orchestrator filters by roster
    /// liveness in memory — the running set is bounded by concurrency × nodes).</summary>
    Task<IReadOnlyList<JobRecord>> Running(CancellationToken ct);

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

    /// <summary>A cheap, bounded global health snapshot (JOBS-0008): a few pushed-down COUNTs + one LIMIT-1 oldest-due
    /// seek — index-served and O(1) in lanes, never a per-lane fan-out. Powers the <c>JobsHealthContributor</c>.</summary>
    Task<JobsHealthSnapshot> HealthSnapshot(DateTimeOffset now, CancellationToken ct);
}
