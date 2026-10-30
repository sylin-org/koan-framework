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

    /// <summary>A queued job of <paramref name="coalesceKey"/> that no worker has claimed — the one a new submission of
    /// that key coalesces onto — or null (JOBS-0010). One indexed read; inside a transaction it also finds a submission
    /// the same transaction made, which is not visible to reads until it commits.</summary>
    Task<JobRecord?> FindCoalescing(string workType, string coalesceKey, CancellationToken ct);

    /// <summary>Append <paramref name="record"/> unless a queued job of its coalesce key exists (JOBS-0010): returns that
    /// job when the record coalesced onto it, or null when the record was appended — never an exception for a coalescing
    /// race. Outside a transaction the check and the append are one conditional insert on the key's unique slot. Inside one
    /// the append is deferred and cannot be conditional (DATA-0131), so the record takes no slot and the commit can never
    /// fail on one: it coalesces onto a committed job or onto one the same transaction submitted, and when two
    /// transactions submit one key at once, the first claim of the key serves both (<see cref="ClaimNext"/>).</summary>
    Task<JobRecord?> AppendCoalescing(JobRecord record, CancellationToken ct);

    /// <summary>Return a claimed job to the queue — a retry, a deferral, a write conflict, a shutdown — as one end-of-claim
    /// owner write (JOBS-0010): it applies only while the stored row carries <paramref name="claimToken"/>, and merges this
    /// execution's own fields onto the row as stored, so a field another writer set stands. A job asked to cancel while it
    /// ran settles <see cref="JobStatus.Cancelled"/> instead. Returns the row as written, or null when the claim was lost;
    /// a write whose answer was lost is recognised by the revision it wrote. Nothing else is written: a submission that
    /// queued beside it is served by the next claim of the key.</summary>
    Task<JobRecord?> TryRequeue(JobRecord record, string claimToken, CancellationToken ct);

    /// <summary>
    /// Atomically claim the next ready job: <c>Status==Queued &amp;&amp; VisibleAt&lt;=now &amp;&amp; CancelRequestedAt==null</c>,
    /// not held for its chain predecessor (<see cref="JobRecord.HeldBy"/>), under a fresh <see cref="JobRecord.ClaimToken"/>,
    /// whose lane is not in <paramref name="saturatedLanes"/> and whose <c>GateKey</c> is not under an active gate;
    /// for pool jobs, elects a free member from <paramref name="pools"/> and stamps it as <c>GateKey</c>;
    /// CAS to <see cref="JobStatus.Running"/>, stamping <paramref name="owner"/> + <paramref name="leaseUntil"/>.
    /// A job with a coalesce key also takes the key's run slot (JOBS-0010): it is not claimable while another job of the
    /// key runs, or while a queued job of the key is deferred past <paramref name="now"/>, so a key never runs twice at
    /// once and never ahead of a deferral; the claimed job then serves every other queued job of its key, each settled
    /// <see cref="JobStatus.Cancelled"/> with <see cref="JobRecord.ServedBy"/> naming it.
    /// Returns null if nothing is claimable. This is the hot path; implementations use their strongest available
    /// concurrency primitive while preserving the documented at-least-once floor.
    /// </summary>
    Task<JobRecord?> ClaimNext(string owner, DateTimeOffset now, DateTimeOffset leaseUntil,
        IReadOnlyCollection<string> saturatedLanes, CancellationToken ct,
        IReadOnlyDictionary<string, PoolDispatchContext>? pools = null);

    /// <summary>Persist a change to a row the caller read — a cancellation, a released reservation, the reaper's reclaim —
    /// as a compare on the <see cref="JobRecord.Revision"/> it read. False when the row changed since, and nothing was
    /// written.</summary>
    Task<bool> Update(JobRecord record, CancellationToken ct);

    /// <summary>
    /// Lease renewal (JOBS-0009, JOBS-0010): push the lease to <paramref name="leaseUntil"/> on the row as stored, only
    /// while it carries <paramref name="claimToken"/>; a field another writer set — a cancellation request — stands.
    /// Returns the row as written, so the owner sees a cancellation another node asked for, or null when the claim was
    /// lost and the caller abandons without settling. A renewal that keeps losing to writes of its own claim's row
    /// returns the row it last read and leaves the lease for the next renewal.
    /// </summary>
    Task<JobRecord?> RenewLease(string jobId, string claimToken, DateTimeOffset leaseUntil, CancellationToken ct);

    /// <summary>
    /// Settlement as one end-of-claim owner write (PMC-055, JOBS-0010): applies <paramref name="record"/>'s terminal state
    /// only while the stored row carries <paramref name="claimToken"/>, merged onto the row as stored so a field another
    /// writer set stands, and records <see cref="JobRecord.SettledBy"/>. Returns the row as written — its cancellation
    /// request decides whether a chain advances — or null when the claim was lost; a settlement whose answer was lost is
    /// recognised by the revision it wrote.
    /// </summary>
    Task<JobRecord?> TrySettle(JobRecord record, string claimToken, CancellationToken ct);

    // --- chain successors (JOBS-0010) ---

    /// <summary>Release the held chain successor <paramref name="successorId"/> when it is still held by
    /// <paramref name="holderToken"/>, or cancel it when <paramref name="cancel"/> — each as a compare on the revision read,
    /// never a copy written over it. A release makes it claimable and moves the wake stamp, as an append does. False when
    /// it is gone, already released or cancelled, or held by another claim.</summary>
    Task<bool> ReleaseHeld(string successorId, string holderToken, bool cancel, DateTimeOffset now, CancellationToken ct);

    /// <summary>Delete the held chain successor <paramref name="successorId"/> when it is still held by
    /// <paramref name="holderToken"/>: its predecessor's claim ended without settling it.</summary>
    Task<bool> DeleteHeld(string successorId, string holderToken, CancellationToken ct);

    /// <summary>A bounded page of held chain successors, for the reaper to find those whose holder claim no longer
    /// exists.</summary>
    Task<IReadOnlyList<JobRecord>> Held(int page, int limit, CancellationToken ct);

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

    /// <summary>Record progress for an in-flight job, merged onto the row as stored and only while it carries
    /// <paramref name="claimToken"/> (cheap, off the transition path).</summary>
    Task Progress(string jobId, string claimToken, double fraction, string? message, CancellationToken ct);

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
    /// a later release extends it, an earlier one never shortens it. Where the store has no atomic insert (SQL Server
    /// today), a gate that does not exist yet is created by an ordinary save and read back, and the writer extends again
    /// when it reads an earlier release than its own; but two writers that create one gate at once can still leave the
    /// earlier of their two releases, when the earlier writer's save lands after the later writer read its own back. The
    /// gate then opens at that earlier time. Where the store has no conditional replace either, an extension is written
    /// the same way, with the same limit.</summary>
    Task SetGate(string gateKey, DateTimeOffset releaseAt, string? reason, CancellationToken ct);

    /// <summary>The gates still closed at <paramref name="now"/> — bounded by the resources currently backing off.</summary>
    Task<IReadOnlyList<JobGateRecord>> ActiveGates(DateTimeOffset now, CancellationToken ct);

    /// <summary>Remove at most <paramref name="limit"/> gates that released at or before <paramref name="now"/>
    /// (JOBS-0010). Where the store deletes conditionally, a gate extended meanwhile is kept; where it does not (SQL Server
    /// today), each gate is re-read just before its delete, and an extension that lands between that read and the delete
    /// is removed with it. Returns the number removed, so a caller pages until it falls short of
    /// <paramref name="limit"/>.</summary>
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
