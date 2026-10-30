using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;

namespace Koan.Jobs;

/// <summary>
/// The enqueued unit of work and the single source of truth — one ledger entry = (work-item × action × lifecycle).
/// Persisted as <c>Entity&lt;JobRecord&gt;</c> so the durable tier rides the existing data layer (no per-DB job
/// adapters; durability follows the host default store). The <c>Id</c> is the Job id; <see cref="WorkId"/>
/// points at the work-item entity. JOBS-0005 §3/§7/§9.
/// </summary>
public sealed class JobRecord : Entity<JobRecord>, IAmbientExempt
{
    /// <summary>Stable key of the discovered <see cref="IKoanJob"/> work-item type (its full name).</summary>
    // JOBS-0005 §19.3 / JOBS-0010: (WorkType,Status,FirstSubmittedAt) serves the newest-first dashboard page and the
    // active count; (WorkType,WorkId,FirstSubmittedAt) a work item's newest job and its history.
    [Index(Group = "ix_jobs_wt_status", Order = 0)]
    [Index(Group = "ix_jobs_wt_workid", Order = 0)]
    public string WorkType { get; set; } = "";

    /// <summary>Id of the work-item entity this job acts on.</summary>
    [Index(Group = "ix_jobs_wt_workid", Order = 1)]
    public string WorkId { get; set; } = "";

    /// <summary>Named data source of the work item. Null denotes the host default.</summary>
    public string? WorkSource { get; set; }

    /// <summary>Data adapter override of the work item. Mutually exclusive with WorkSource.</summary>
    public string? WorkAdapter { get; set; }

    /// <summary>Physical partition of the work item. Null denotes the default partition.</summary>
    public string? WorkPartition { get; set; }

    /// <summary>The action/stage being executed (empty for single-action jobs).</summary>
    public string Action { get; set; } = "";

    // JOBS-0005 §19.3: the claim loop's full sort key — Status prefix, then the (VisibleAt, FirstSubmittedAt) order.
    // JOBS-0008: also the (Lane, Status, VisibleAt, FirstSubmittedAt) index serving the per-lane head seek.
    [Index(Group = "ix_jobs_claim", Order = 0)]
    [Index(Group = "ix_jobs_wt_status", Order = 1)]
    [Index(Group = "ix_jobs_lane_claim", Order = 1)]
    [Index(Group = "ix_jobs_settled", Order = 0)]
    [Index(Group = "ix_jobs_coalesce_group", Order = 1)]
    public JobStatus Status { get; set; } = JobStatus.Created;

    /// <summary>Failure-and-retry count (distinct from <see cref="Reschedules"/>).</summary>
    public int Attempt { get; set; }

    /// <summary>Cooperative-deferral count (Reschedule != Retry).</summary>
    public int Reschedules { get; set; }

    /// <summary>Runs whose changes conflicted with another writer's (JOBS-0010) — neither a failed attempt nor a
    /// deferral; bounded by <see cref="JobsOptions.MaxWriteConflicts"/>.</summary>
    public int Conflicts { get; set; }

    /// <summary>Ready to claim when <c>VisibleAt &lt;= now</c> (future for delayed/deferred jobs).</summary>
    [Index(Group = "ix_jobs_claim", Order = 1)]
    [Index(Group = "ix_jobs_lane_claim", Order = 2)]
    public DateTimeOffset VisibleAt { get; set; }

    [Index(Group = "ix_jobs_claim", Order = 2)]
    [Index(Group = "ix_jobs_lane_claim", Order = 3)]
    [Index(Group = "ix_jobs_wt_status", Order = 2)]
    [Index(Group = "ix_jobs_wt_workid", Order = 2)]
    public DateTimeOffset FirstSubmittedAt { get; set; }

    /// <summary>When the job last settled. With Status it orders the retention sweep's pages (JOBS-0010).</summary>
    [Index(Group = "ix_jobs_settled", Order = 1)]
    public DateTimeOffset? LastSettledAt { get; set; }

    /// <summary>Absolute time after which this terminal row may be deleted (JOBS-0005 §20.4) — set at settle per outcome
    /// (Completed/Cancelled → +ArchiveAfter, Failed/Dead → +FailedAfter). On a TTL-capable store (Mongo) the
    /// <c>[Index(Ttl)]</c> makes the store expire the row automatically; elsewhere the periodic purge handles it. Null
    /// (non-terminal) is never expired.</summary>
    [Index(Group = "ix_jobs_ttl", Ttl = true)]
    public DateTimeOffset? ExpireAt { get; set; }

    /// <summary>Concurrency lane (defaults to the action name). The leading key of the JOBS-0008 per-lane head index.</summary>
    [Index(Group = "ix_jobs_lane_claim", Order = 0)]
    public string Lane { get; set; } = "";

    // --- lease (claim ownership) ---
    public string? Owner { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }

    // --- reservation (dispatch hand-off, PMC-056) ---
    /// <summary>The named roster node this Queued row is reserved for (<c>DispatchMode.Reservation</c> only).
    /// The assignment is dispatch metadata, not lifecycle: the row stays <see cref="JobStatus.Queued"/>, writes no
    /// transition, and raises no event. Claimable by this hand while fresh; open to any hand once lapsed or when the
    /// coordinator releases a dead hand's reservation. Consumed at claim together with Owner.</summary>
    public string? ReservedFor { get; set; }
    public DateTimeOffset? ReservedUntil { get; set; }

    // --- coalesce / idempotency ---
    public string? CoalesceKey { get; set; }

    private Guid? _coalesceSlot;

    /// <summary>The coalescing slot this row holds (JOBS-0010): <see cref="SlotFor"/> its coalesce key while it is a
    /// submission no worker has claimed yet, a slot derived from its own id otherwise. The slot is unique, so a second
    /// unclaimed submission of the same key cannot be appended beside the first: the coalescing check and the append are
    /// one conditional insert. A claim, a cancellation before running, and every later state give the slot back. A
    /// fixed-width GUID, so every store builds the unique index.</summary>
    [Index(Group = "ux_jobs_coalesce_slot", Unique = true)]
    public Guid CoalesceSlot
    {
        get => _coalesceSlot ?? JobKeys.GuidOf("job-", Id);
        set => _coalesceSlot = value;
    }

    /// <summary>The coalescing group of every job of this row's coalesce key, unclaimed or not (JOBS-0010): what
    /// coalescing looks a key up by, on a fixed-width indexed value. Null for a job without a coalesce key.</summary>
    [Index(Group = "ix_jobs_coalesce_group", Order = 0)]
    public Guid? CoalesceGroup => CoalesceKey is { } key ? SlotFor(key) : null;

    /// <summary>The slot an unclaimed submission of <paramref name="coalesceKey"/> holds.</summary>
    public static Guid SlotFor(string coalesceKey) => JobKeys.GuidOf("coalesce-", coalesceKey);

    /// <summary>True while this row holds its coalesce key's slot.</summary>
    internal bool HoldsCoalesceSlot => CoalesceKey is { } key && _coalesceSlot == SlotFor(key);

    /// <summary>Give the coalescing slot back: a later submission of the same key queues beside this row.</summary>
    internal void ReleaseCoalesceSlot() => _coalesceSlot = null;

    private Guid? _runSlot;

    /// <summary>The run slot this row holds (JOBS-0010): its coalesce key's <see cref="CoalesceGroup"/> while the job runs,
    /// a slot derived from its own id otherwise. The slot is unique, so a second job of the same key cannot be claimed
    /// while one runs: the claim and the key's exclusivity are one conditional write. Every write that takes the job out
    /// of Running gives the slot back; a crashed runner's slot is given back when the reaper requeues its lapsed lease.</summary>
    [Index(Group = "ux_jobs_run_slot", Unique = true)]
    public Guid RunSlot
    {
        get => _runSlot ?? JobKeys.GuidOf("run-", Id);
        set => _runSlot = value;
    }

    /// <summary>True while this row holds its coalesce key's run slot.</summary>
    internal bool HoldsRunSlot => CoalesceGroup is { } group && _runSlot == group;

    /// <summary>Take the coalesce key's run slot, as a claim does; a job without a coalesce key holds its own.</summary>
    internal void TakeRunSlot()
    {
        if (CoalesceGroup is { } group) _runSlot = group;
    }

    /// <summary>Give the run slot back: another job of the key may be claimed.</summary>
    internal void ReleaseRunSlot() => _runSlot = null;

    /// <summary>The row's revision (JOBS-0010): a fresh value on every ledger write, so each write is a compare on the
    /// revision its writer read and a stale copy never lands where the store replaces conditionally.</summary>
    public Guid Revision { get; set; }

    /// <summary>The current claim's token (JOBS-0010): a fresh random value written by each claim and kept by its
    /// renewals. Every write of the claim's owner — renewal, progress, settlement, requeue — applies only while the row
    /// carries it, so an execution whose claim was taken over, on another node or its own, writes nothing. Every write
    /// that ends the claim drops it.</summary>
    public string? ClaimToken { get; set; }

    /// <summary>The token of the claim whose settlement ended this job (JOBS-0010): what tells the reaper which run's held
    /// successor a settlement released.</summary>
    public string? SettledBy { get; set; }

    /// <summary>The claim token of the predecessor that appended this chain successor and has not settled yet
    /// (JOBS-0010). A held job is queued but never claimed, reserved, coalesced onto or absorbed; its predecessor's
    /// settlement releases it or cancels it, and a requeue of the predecessor deletes it.</summary>
    [Index(Group = "ix_jobs_held")]
    public string? HeldBy { get; set; }

    /// <summary>The predecessor a held successor waits for (JOBS-0010).</summary>
    public string? HeldFor { get; set; }

    /// <summary>The settlement a held successor was appended for (JOBS-0010): <see cref="JobStatus.Completed"/> on the
    /// success path, <see cref="JobStatus.Failed"/> under <c>OnFailure.Continue</c>. Only that settlement of its
    /// predecessor, by the claim that holds it, lets the reaper release it once that claim is gone.</summary>
    public JobStatus? HeldOn { get; set; }

    /// <summary>The id of the chain successor one claim of <paramref name="predecessorId"/> appends: derived from the
    /// predecessor and that claim's token, so one run's appends write one row and two runs never collide (JOBS-0010).</summary>
    public static string SuccessorId(string predecessorId, string claimToken)
        => JobKeys.GuidOf("successor-", predecessorId + "/" + claimToken).ToString("N");

    // --- gate (cooperative backoff / dispatch-time pool) ---
    /// <summary>The resolved resource key for this job: set at submit for <c>[JobGate]</c> types; stamped at
    /// claim time for <c>[JobPool]</c> types (the chosen pool member). Null means no gate constraint.</summary>
    public string? GateKey { get; set; }

    /// <summary>Pool name for dispatch-time pool allocation (<c>[JobPool]</c>). Null for non-pool jobs. When set,
    /// GateKey starts null at submit and is stamped with the chosen member key atomically at claim (JOBS-0007).</summary>
    public string? PoolKey { get; set; }

    /// <summary>When true (default), this job holds its work-item exclusively: no other job for the same
    /// <c>(WorkType, WorkId)</c> may run concurrently. False for <c>[ParallelSafe]</c> types (JOBS-0005 §17.2).</summary>
    public bool Exclusive { get; set; } = true;

    // --- cancellation (durable marker) ---
    public DateTimeOffset? CancelRequestedAt { get; set; }

    // --- diagnostics / observability ---
    public string? LastError { get; set; }

    /// <summary>The job that serves this submission, when it was coalesced into another job after it queued (JOBS-0010).
    /// The row settles <see cref="JobStatus.Cancelled"/>; a handle waiting on it follows the serving job and reports that
    /// job's outcome.</summary>
    public string? ServedBy { get; set; }
    public string? DeferReason { get; set; }
    public DateTimeOffset? Deadline { get; set; }
    public string? CorrelationId { get; set; }

    /// <summary>The Koan context (tenant, access subject, …) captured at submit, keyed by axis — restored before the
    /// handler runs so the job executes in the context it was submitted under across the async hop. Null when no
    /// cross-cutting axis was in scope (the hot-path common case; absent on the row). Carried opaquely: this entity
    /// names no axis. The property name is retained for persisted-wire compatibility. Immutable after capture;
    /// deep-copied by <see cref="Clone"/>.</summary>
    public Dictionary<string, string>? AmbientCarrier { get; set; }

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
        c.Transitions = Transitions.ConvertAll(t => new JobTransition { At = t.At, From = t.From, To = t.To, Note = t.Note, Revision = t.Revision });
        // Deep-copy the carrier bag: the in-memory ledger stores/returns clones, so a shared dictionary instance
        // would alias the captured context across jobs.
        c.AmbientCarrier = AmbientCarrier is null ? null : new Dictionary<string, string>(AmbientCarrier);
        return c;
    }

    /// <summary>
    /// The write that ends a claim, merged onto the row as stored (JOBS-0010): the fields this execution owns come from
    /// <paramref name="decided"/> — status, visible time, ownership, lease and reservation, settlement and expiry, the
    /// reasons and last error, the attempt, reschedule and conflict counts, its last progress and its transitions — and
    /// everything another writer set stays as <paramref name="latest"/> holds it, a cancellation request above all. The
    /// claim token and both slots are given back. Asked to cancel while it ran, a job its execution would requeue — a
    /// retry, a deferral, a conflict, a shutdown — settles Cancelled instead; a completion or a failure settles as decided.
    /// </summary>
    internal static JobRecord EndOfClaim(JobRecord latest, JobRecord decided, string claimToken, TimeSpan archiveAfter)
    {
        var row = latest.Clone();
        row.Status = decided.Status;
        row.VisibleAt = decided.VisibleAt;
        row.Owner = decided.Owner;
        row.LeaseUntil = decided.LeaseUntil;
        row.ReservedFor = decided.ReservedFor;
        row.ReservedUntil = decided.ReservedUntil;
        row.LastSettledAt = decided.LastSettledAt;
        row.ExpireAt = decided.ExpireAt;
        row.DeadReason = decided.DeadReason;
        row.DeferReason = decided.DeferReason;
        row.LastError = decided.LastError;
        row.Attempt = decided.Attempt;
        row.Reschedules = decided.Reschedules;
        row.Conflicts = decided.Conflicts;
        row.ProgressFraction = decided.ProgressFraction;
        row.ProgressMessage = decided.ProgressMessage;
        row.Transitions = decided.Transitions.ConvertAll(t => new JobTransition { At = t.At, From = t.From, To = t.To, Note = t.Note, Revision = t.Revision });
        row.ClaimToken = null;
        row.ReleaseCoalesceSlot();
        row.ReleaseRunSlot();
        row.SettledBy = row.IsTerminal ? claimToken : null;

        if (row.Status == JobStatus.Queued && row.CancelRequestedAt is not null)
        {
            var at = row.Transitions.Count > 0 ? row.Transitions[^1].At : decided.LastSettledAt ?? decided.VisibleAt;
            var instead = row.Transitions.Count > 0 ? row.Transitions[^1].Note : null;
            if (row.Transitions.Count > 0) row.Transitions.RemoveAt(row.Transitions.Count - 1);
            row.Transitions.Add(new JobTransition
            {
                At = at, From = JobStatus.Running, To = JobStatus.Cancelled,
                Note = instead is null ? "cancelled (asked while it ran)" : $"cancelled (asked while it ran; instead of: {instead})",
            });
            row.Status = JobStatus.Cancelled;
            row.LastSettledAt = at;
            row.ExpireAt = archiveAfter > TimeSpan.Zero ? at + archiveAfter : null;
            row.DeferReason = null;
            row.SettledBy = claimToken;
        }
        return row;
    }
}
