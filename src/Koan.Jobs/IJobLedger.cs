namespace Koan.Jobs;

/// <summary>
/// The single source of truth and the single writer (JOBS-0005 §7). The ledger <em>is</em> the queue: dispatch
/// claims the next ready row directly; there is no separate volatile queue to reconcile. Every write to a stored row is
/// a compare on <see cref="JobRecord.Revision"/>, the revision its writer read (JOBS-0010): data-backed implementations
/// make it one conditional replace where the store offers one, so a stale copy never lands, and fall back to a read and
/// compare just before an ordinary save otherwise — the explicit at-least-once floor. Physical layout (in-memory,
/// data-backed, hot/cold partitions) hides behind this interface; the orchestrator is storage-agnostic.
/// </summary>
public interface IJobLedger
{
    /// <summary>Append a new job (Submit). For the durable tier this participates in the ambient transaction (outbox).</summary>
    Task Append(JobRecord record, CancellationToken ct);

    /// <summary>Append a batch in one shot.</summary>
    Task AppendMany(IReadOnlyCollection<JobRecord> records, CancellationToken ct);

    Task<JobRecord?> Get(string jobId, CancellationToken ct);

    /// <summary>A queued job of <paramref name="coalesceKey"/> — the one a new submission of that key coalesces onto — or
    /// null (JOBS-0010). One indexed read; inside a transaction it also finds a submission the same transaction made, which
    /// is not visible to reads until it commits.</summary>
    Task<JobRecord?> FindCoalescing(string workType, string coalesceKey, CancellationToken ct);

    /// <summary>Append <paramref name="record"/> unless a queued job of its coalesce key exists (JOBS-0010): returns that
    /// job when the record coalesced onto it, or null when the record was appended — never an exception for a coalescing
    /// race. Outside a transaction the check and the append are one conditional insert on the key's unique slot. Inside one
    /// the append is deferred and cannot be conditional (DATA-0131), so the record takes no slot, and two transactions
    /// that submit one key at once each commit a job.</summary>
    Task<JobRecord?> AppendCoalescing(JobRecord record, CancellationToken ct);

    /// <summary>
    /// Atomically claim the next ready job: <c>Status==Queued &amp;&amp; VisibleAt&lt;=now &amp;&amp; CancelRequestedAt==null</c>,
    /// whose lane is not in <paramref name="saturatedLanes"/>, whose work item no other job holds exclusively, and whose
    /// <c>GateKey</c> is not under an active gate; for pool jobs, elects a free member from <paramref name="pools"/> and
    /// stamps it as <c>GateKey</c>; compare to <see cref="JobStatus.Running"/>, stamping <paramref name="owner"/> +
    /// <paramref name="leaseUntil"/>. Returns null if nothing is claimable. This is the hot path; implementations use their
    /// strongest available concurrency primitive while preserving the documented at-least-once floor.
    /// </summary>
    Task<JobRecord?> ClaimNext(string owner, DateTimeOffset now, DateTimeOffset leaseUntil,
        IReadOnlyCollection<string> saturatedLanes, CancellationToken ct,
        IReadOnlyDictionary<string, PoolDispatchContext>? pools = null);

    /// <summary>Persist a change to a row the caller read — every write to a stored row after its append — as a compare on
    /// the <see cref="JobRecord.Revision"/> it read. False when the row changed since, and nothing was written. A row
    /// written settled is told to the handles waiting on it.</summary>
    Task<bool> Update(JobRecord record, CancellationToken ct);

    // --- reservation dispatch (PMC-056) ---

    /// <summary>
    /// Guarded assignment (PMC-056): stamp <c>ReservedFor=<paramref name="hand"/></c> on a Queued row only while it is
    /// still Queued and unreserved. The assignment is ledger-verifiable dispatch metadata (no side queues, no routing
    /// state): the row stays Queued and writes no transition. A false return means another coordinator won the stamp or
    /// the row moved on.
    /// </summary>
    Task<bool> TryReserve(string jobId, string hand, DateTimeOffset reservedUntil, DateTimeOffset now, CancellationToken ct);

    /// <summary>The oldest due, unreserved, non-cancelled Queued rows in dispatch order — what an active
    /// coordinator considers for assignment, bounded by <paramref name="limit"/> so a deep backlog costs one
    /// indexed seek, never a scan.</summary>
    Task<IReadOnlyList<JobRecord>> ReservationCandidates(DateTimeOffset now, int limit, CancellationToken ct);

    /// <summary>All currently-reserved non-terminal rows regardless of hand or age — bounded by outstanding
    /// assignments (fleet capacity), never by backlog size. The coordinator sweeps this for lapses and dead hands.</summary>
    Task<IReadOnlyList<JobRecord>> Reservations(CancellationToken ct);

    /// <summary>At most <paramref name="limit"/> Running jobs whose lease lapsed (the reaper sweep, a page at a time).</summary>
    Task<IReadOnlyList<JobRecord>> Stuck(DateTimeOffset now, int limit, CancellationToken ct);

    /// <summary>At most <paramref name="limit"/> Running jobs owned by a worker not in <paramref name="liveOwners"/>
    /// (PMC-055 death sweep, a page at a time).</summary>
    Task<IReadOnlyList<JobRecord>> Orphaned(IReadOnlyCollection<string> liveOwners, int limit, CancellationToken ct);

    /// <summary>How many Running jobs each of <paramref name="owners"/> holds (PMC-056 hand load) — bounded by those
    /// owners' concurrency, never by the backlog.</summary>
    Task<IReadOnlyDictionary<string, int>> RunningLoad(IReadOnlyCollection<string> owners, CancellationToken ct);

    /// <summary>The declarative facade/dashboard query: a wildcard-axis filter identical on every tier, read newest
    /// first by <c>FirstSubmittedAt</c> one page at a time — <see cref="JobQuery.Page"/> of <see cref="JobQuery.Size"/>
    /// matches (JOBS-0010).</summary>
    Task<IReadOnlyList<JobRecord>> Query(JobQuery query, CancellationToken ct);

    // --- shared resource gates (cooperative backoff) ---

    /// <summary>Close the gate for <paramref name="gateKey"/> until <paramref name="releaseAt"/>. One key is one gate:
    /// a later release extends it, an earlier one never shortens it, as read just before the write. Two writers at once
    /// can leave the earlier release; the gate then opens early and the next backoff closes it again.</summary>
    Task SetGate(string gateKey, DateTimeOffset releaseAt, string? reason, CancellationToken ct);

    /// <summary>The gates still closed at <paramref name="now"/> — bounded by the resources currently backing off.</summary>
    Task<IReadOnlyList<JobGateRecord>> ActiveGates(DateTimeOffset now, CancellationToken ct);

    /// <summary>Remove at most <paramref name="limit"/> gates that released at or before <paramref name="now"/>
    /// (JOBS-0010). Returns the number removed, so a caller pages until it falls short of <paramref name="limit"/>.</summary>
    Task<int> PurgeReleasedGates(DateTimeOffset now, int limit, CancellationToken ct);

    /// <summary>Remove at most <paramref name="limit"/> benign terminal rows (Completed/Cancelled) settled before
    /// <paramref name="olderThan"/>, keeping the active set lean. Returns the number removed, so a caller pages until it
    /// falls short of <paramref name="limit"/> (JOBS-0010).</summary>
    Task<int> PurgeArchivable(DateTimeOffset olderThan, int limit, CancellationToken ct);

    /// <summary>Remove at most <paramref name="limit"/> Failed/Dead rows settled before <paramref name="olderThan"/>
    /// (replayable until then) — the §19.3 completion of retention so failures don't accumulate forever. Returns the
    /// number removed.</summary>
    Task<int> PurgeFailed(DateTimeOffset olderThan, int limit, CancellationToken ct);

    /// <summary>Remove at most <paramref name="limit"/> of a work-type's terminal rows older than its newest
    /// <paramref name="keep"/> (the per-work-type count cap, §19.3). Returns the number removed.</summary>
    Task<int> TrimTerminal(string workType, int keep, int limit, CancellationToken ct);

    /// <summary>Count active (non-terminal) rows for a work-type — the cheap pushed probe behind the §19.4
    /// job-per-row guardrail.</summary>
    Task<long> CountActive(string workType, CancellationToken ct);

    /// <summary>A cheap, bounded global health snapshot (JOBS-0008): a few pushed-down COUNTs + one LIMIT-1 oldest-due
    /// seek — index-served and O(1) in lanes, never a per-lane fan-out. Powers the <c>JobsHealthContributor</c>.</summary>
    Task<JobsHealthSnapshot> HealthSnapshot(DateTimeOffset now, CancellationToken ct);
}
