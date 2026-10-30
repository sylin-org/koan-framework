using System.Linq.Expressions;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Abstractions.Failures;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Core;
using Koan.Data.Core.Failures;
using Koan.Data.Core.Sorting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Jobs;

/// <summary>
/// Durable <see cref="IJobLedger"/> riding the existing data layer — the ledger and gates are each
/// an <see cref="Koan.Data.Core.Model.Entity{T}"/> persisted in the host default store, so there are no per-DB job
/// adapters and durability follows whatever data provider is present (JOBS-0005 §7/§8). A provider-declared
/// conditional replace is the atomic claim primitive; adapters without it retain the honest optimistic
/// at-least-once fallback.
/// </summary>
internal sealed class DataJobLedger : IJobLedger
{
    private readonly JobsOptions _options;
    private readonly JobTypeRegistry _registry;
    // JOBS-0008: per-node WFQ virtual time held in-process (contention-free). Deliberately NOT a durable shared row:
    // a per-claim CAS on a shared per-lane cursor is a write-contention hotspot on the dispatch hot path (SQLite
    // surfaces it as 'database is locked'; every store pays it as serialization). Per-node WFQ is starvation-free
    // GLOBALLY — every node fairly multiplexes the lanes it claims — which is the requirement. Exact global weight
    // proportions under skewed multi-node feed are out of scope here; if ever needed, use node-sharded batched state
    // (the JobMetric pattern), never a per-claim shared write.
    private readonly Dictionary<string, double> _virtual = new(StringComparer.Ordinal);
    private readonly Func<string, double> _weight;
    private readonly IReadOnlyList<IDataFailureClassifier> _classifiers;
    private readonly JobCompletions? _completions;
    private readonly Microsoft.Extensions.Logging.ILogger? _logger;

    /// <summary>Attempts one claim gets when the store reports a transient conflict. Two is enough for a
    /// deadlock — the winner has committed by the time the victim rolls back — and the third is headroom for a
    /// second collision under contention rather than a policy of grinding.</summary>
    private const int ClaimAttempts = 3;

    /// <summary>Reads a gate write makes against concurrent writers of the same key before it lets theirs stand.</summary>
    private const int GateAttempts = 3;

    /// <summary>Appends a coalescing submission makes against concurrent holders of its key's slot.</summary>
    private const int CoalesceAttempts = 3;

    /// <summary>Reads an owner's write (a settlement, a requeue, a lease renewal, a progress note) makes against concurrent
    /// writes to its own running row — a lease renewal or a cancellation request landing between its read and its write.</summary>
    private const int FenceAttempts = 8;

    public DataJobLedger(
        IOptions<JobsOptions> options,
        JobTypeRegistry registry,
        IEnumerable<IDataFailureClassifier>? classifiers = null,
        JobCompletions? completions = null,
        Microsoft.Extensions.Logging.ILogger<DataJobLedger>? logger = null)
    {
        _completions = completions;
        _logger = logger;
        _options = options.Value;
        _registry = registry;
        _weight = lane => _options.LaneWeights.GetValueOrDefault(lane, 1.0);
        _classifiers = classifiers?.ToArray() ?? [];
    }

    public async Task Append(JobRecord record, CancellationToken ct)
    {
        using var storageScope = JobsStorageScope.Enter();
        record.Revision = Guid.NewGuid();
        await JobRecord.Upsert(record, ct);
        await WakeStamp.TryBump(ct);   // JOBS-0009: same ambient transaction — rollback never moves the stamp
    }

    public async Task AppendMany(IReadOnlyCollection<JobRecord> records, CancellationToken ct)
    {
        using var storageScope = JobsStorageScope.Enter();
        foreach (var record in records) record.Revision = Guid.NewGuid();
        await JobRecord.UpsertMany(records, ct);
        await WakeStamp.TryBump(ct);   // one bump per batch, not per row
    }

    public async Task<JobRecord?> Get(string jobId, CancellationToken ct)
    {
        using var storageScope = JobsStorageScope.Enter();
        return await JobRecord.Get(jobId, ct);
    }

    public async Task<JobRecord?> FindCoalescing(string workType, string coalesceKey, CancellationToken ct)
    {
        using var storageScope = JobsStorageScope.Enter();
        var group = JobRecord.SlotFor(coalesceKey);
        // A claimed job gave its key back, so a submission arriving while it runs queues a trailing execution (JOBS-0006).
        // Every queued job of the key counts, the slot holder or not; inside a transaction so do the submissions this
        // transaction already made, which are not visible until it commits.
        return (await QueuedOf(group, 1, ct)).FirstOrDefault() ?? PendingIn(EntityContext.CurrentTransaction, group);
    }

    public async Task<JobRecord?> AppendCoalescing(JobRecord record, CancellationToken ct)
    {
        if (record.CoalesceKey is not { } key)
        {
            await Append(record, ct);
            return null;
        }

        using var storageScope = JobsStorageScope.Enter();
        var group = JobRecord.SlotFor(key);
        if (EntityContext.CurrentTransaction is { IsCompleted: false } transaction)
        {
            // A deferred write cannot be conditional (DATA-0131), so the record takes no slot and the commit can never fail
            // on one: it coalesces onto a committed job of its key or onto one this transaction already submitted. Two
            // transactions that submit one key at once both commit a job; the first of them claimed serves the other.
            if (((await QueuedOf(group, 1, ct)).FirstOrDefault() ?? PendingIn(transaction, group)) is { } queued) return queued;
            await Append(record, ct);
            Pending.GetOrCreateValue(transaction)[group] = record;
            return null;
        }

        // The check and the append are one conditional insert: the record holds its key's unique slot, so a second
        // unclaimed submission of the key cannot be appended beside it. A refused append whose holder has meanwhile been
        // claimed or cancelled finds none and appends again; the outcome is always an append or a coalesce.
        for (var attempt = 1; attempt <= CoalesceAttempts; attempt++)
        {
            if ((await QueuedOf(group, 1, ct)).FirstOrDefault() is { } queued) return queued;
            record.CoalesceSlot = group;
            try
            {
                await Append(record, ct);
                return null;
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                record.ReleaseCoalesceSlot();
            }
        }

        // The slot kept changing hands, or the store refuses the write for another reason, which this append surfaces:
        // append without the slot; the next claim of the key serves whatever queued beside it.
        if ((await QueuedOf(group, 1, ct)).FirstOrDefault() is { } holder) return holder;
        await Append(record, ct);
        return null;
    }

    /// <summary>One end-of-claim owner write (JOBS-0010); asked to cancel while it ran, the job settles Cancelled instead.
    /// Nothing else is written: the next claim of the key serves whatever queued beside it.</summary>
    public async Task<JobRecord?> TryRequeue(JobRecord record, string claimToken, CancellationToken ct)
    {
        using var storageScope = JobsStorageScope.Enter();
        var written = await EndClaim(record, claimToken, ct);
        if (written is not null) Heard(written);
        return written;
    }

    /// <summary>The write that ends a claim (JOBS-0010): read the row; when it no longer carries
    /// <paramref name="claimToken"/>, the claim is lost — unless the row is one of this call's own writes whose answer was
    /// lost, recognised by its revision or by the transition that carries it. Otherwise merge this execution's own
    /// fields onto the row just read (<see cref="JobRecord.EndOfClaim"/>), write it as a compare on that read's revision,
    /// and read again on a miss, a bounded number of times. Null when the claim was lost or kept losing.</summary>
    private async Task<JobRecord?> EndClaim(JobRecord decided, string claimToken, CancellationToken ct)
    {
        var attempts = new List<(Guid Revision, JobRecord Row)>();
        for (var attempt = 1; ; attempt++)
        {
            var latest = await JobRecord.Get(decided.Id, ct);
            if (latest is null) return null;
            if (!string.Equals(latest.ClaimToken, claimToken, StringComparison.Ordinal))
                return Landed(latest, attempts);
            if (attempt > FenceAttempts) return null;

            var row = JobRecord.EndOfClaim(latest, decided, claimToken, _options.ArchiveAfter);
            var revision = Guid.NewGuid();
            if (row.Transitions.Count > 0) row.Transitions[^1].Revision = revision;
            attempts.Add((revision, row));
            try
            {
                if (await ReplaceAtRevision(row, latest.Revision, ct, revision))
                {
                    decided.Revision = revision;
                    return row;
                }
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                // The write may have landed with its answer lost: the next read tells.
            }
        }
    }

    /// <summary>The write of this call that <paramref name="latest"/> shows landed, or null.</summary>
    private static JobRecord? Landed(JobRecord latest, List<(Guid Revision, JobRecord Row)> attempts)
    {
        foreach (var (revision, row) in attempts)
            if (latest.Revision == revision || latest.Transitions.Any(t => t.Revision == revision))
            {
                row.Revision = revision;
                return row;
            }
        return null;
    }

    /// <summary>Write <paramref name="record"/> over the stored row only while that row still carries <paramref name="read"/>,
    /// the revision its writer read (JOBS-0010); the record takes a fresh revision. Where the store replaces conditionally
    /// and no transaction defers the write, that is one conditional replace, so a stale copy never lands. Elsewhere the
    /// stored revision is read and compared just before an ordinary save: the at-least-once floor, where a write landing
    /// between that read and the save is overwritten.</summary>
    private static async Task<bool> ReplaceAtRevision(JobRecord record, Guid read, CancellationToken ct, Guid? revision = null)
    {
        record.Revision = revision ?? Guid.NewGuid();
        try
        {
            bool landed;
            if (!EntityContext.InTransaction && ConditionalWriter() is { } cas)
            {
                landed = await cas.ConditionalReplaceAsync(record,
                    Koan.Data.Abstractions.Filtering.LinqFilterCompiler.Compile<JobRecord>(r => r.Revision == read), ct);
            }
            else
            {
                var stored = await JobRecord.Get(record.Id, ct);
                landed = stored is not null && stored.Revision == read;
                if (landed) await JobRecord.Upsert(record, ct);
            }
            if (!landed) record.Revision = read;
            return landed;
        }
        catch
        {
            record.Revision = read;
            throw;
        }
    }

    private static IConditionalWriteRepository<JobRecord, string>? ConditionalWriter()
        => Data<JobRecord, string>.Capabilities.Has(DataCaps.Write.ConditionalReplace)
            ? Data<JobRecord, string>.As<IConditionalWriteRepository<JobRecord, string>>()
            : null;

    /// <summary>The claimed job holds its key's run slot, so no other job of the key can be claimed — or deferred — while
    /// it decides (JOBS-0010). A queued job of the key deferred past now means the key's work waits for that deferral: the
    /// claim carries the later visible time back to the queue instead of running. That happens only when the deferral
    /// landed between the claim's read and its write; a claim that reads one waits without writing. Otherwise this run
    /// serves every other queued job of the key, which settles as coalesced into it. False when the claim went back.</summary>
    private async Task<bool> ServeKey(JobRecord claimed, Guid group, DateTimeOffset now, CancellationToken ct)
    {
        var others = (await QueuedOf(group, Infrastructure.Constants.Retention.PageSize, ct))
            .Where(other => other.Id != claimed.Id).ToList();
        if (others.Where(other => other.VisibleAt > now).Select(other => (DateTimeOffset?)other.VisibleAt).Max() is { } deferredUntil)
        {
            var decided = claimed.Clone();
            decided.Attempt = Math.Max(0, decided.Attempt - 1);
            decided.Transitions.Add(new JobTransition
            {
                At = now, From = JobStatus.Running, To = JobStatus.Queued,
                Note = $"waits for its key's deferral until {deferredUntil:O}",
            });
            decided.Status = JobStatus.Queued;
            decided.Owner = null;
            decided.LeaseUntil = null;
            decided.VisibleAt = deferredUntil;
            var written = await EndClaim(decided, claimed.ClaimToken!, ct);
            if (written is not null)
            {
                Heard(written);   // asked to cancel meanwhile, it settled Cancelled
                return false;
            }
            // Neither landed nor lost: the store kept refusing the write-back. The claim never runs ahead of the deferral;
            // it is left to lapse with its lease, and the reaper requeues it.
            _logger?.LogWarning(
                "Koan.Jobs could not return job {JobId} to the queue to wait for its key's deferral; the claim is left to lapse with its lease ({LeaseUntil:O}), and the reaper requeues it.",
                claimed.Id, claimed.LeaseUntil);
            return false;
        }

        foreach (var other in others)
        {
            try
            {
                await Absorb(other, claimed.Id, now, ct);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                // It stays queued, and the key's run slot makes it run after this job, not beside it.
            }
        }
        return true;
    }

    /// <summary>The queued, uncancelled jobs of a coalescing group that no predecessor holds: one indexed read, at most
    /// <paramref name="limit"/>.</summary>
    private static Task<IReadOnlyList<JobRecord>> QueuedOf(Guid group, int limit, CancellationToken ct)
        => JobRecord.Query(
            r => r.CoalesceGroup == group && r.Status == JobStatus.Queued && r.CancelRequestedAt == null && r.HeldBy == null,
            QueryDefinition.All.WithPagination(1, limit), ct);

    /// <summary>True when a job of the key other than <paramref name="jobId"/> runs, holding the key's run slot.</summary>
    private static async Task<bool> KeyRuns(Guid group, string jobId, CancellationToken ct)
        => (await JobRecord.Query(r => r.CoalesceGroup == group && r.Status == JobStatus.Running,
                QueryDefinition.All.WithPagination(1, 2), ct))
            .Any(row => !string.Equals(row.Id, jobId, StringComparison.Ordinal));

    // --- chain successors (JOBS-0010) ---

    public async Task<bool> ReleaseHeld(string successorId, string holderToken, bool cancel, DateTimeOffset now, CancellationToken ct)
    {
        using var storageScope = JobsStorageScope.Enter();
        var attempted = new List<(Guid Revision, JobRecord Row)>();
        for (var attempt = 1; attempt <= FenceAttempts; attempt++)
        {
            var held = await JobRecord.Get(successorId, ct);
            // A release whose answer was lost is recognised by the revision it wrote, as an owner's write is.
            if (held is not null && attempted.FirstOrDefault(a => a.Revision == held.Revision) is { Row: not null } own)
            {
                if (cancel) Heard(own.Row);
                else await WakeStamp.TryBump(ct);
                return true;
            }
            if (held is not { Status: JobStatus.Queued } || !string.Equals(held.HeldBy, holderToken, StringComparison.Ordinal))
                return false;
            var row = held.Clone();
            row.HeldBy = null;
            if (cancel)
            {
                row.Transitions.Add(new JobTransition { At = now, From = JobStatus.Queued, To = JobStatus.Cancelled, Note = "cancelled with its predecessor" });
                row.Status = JobStatus.Cancelled;
                row.LastSettledAt = now;
                row.ExpireAt = _options.ArchiveAfter > TimeSpan.Zero ? now + _options.ArchiveAfter : null;
            }
            var revision = Guid.NewGuid();
            attempted.Add((revision, row));
            bool landed;
            try
            {
                landed = await ReplaceAtRevision(row, held.Revision, ct, revision);
            }
            catch (Exception) when (!ct.IsCancellationRequested && attempt < FenceAttempts)
            {
                continue;   // the next read tells whether it landed
            }
            if (!landed) continue;
            if (cancel) Heard(row);
            else await WakeStamp.TryBump(ct);   // released work moves the stamp, as an append does
            return true;
        }
        return false;
    }

    public async Task<bool> DeleteHeld(string successorId, string holderToken, CancellationToken ct)
    {
        using var storageScope = JobsStorageScope.Enter();
        var held = await JobRecord.Get(successorId, ct);
        if (held is null || !string.Equals(held.HeldBy, holderToken, StringComparison.Ordinal)) return false;
        // The id derives from the holder's claim token, so no other run's successor can carry it: a read before an
        // ordinary removal is safe where the store cannot delete conditionally.
        var guarded = Data<JobRecord, string>.As<IConditionalDeleteRepository<JobRecord, string>>();
        if (guarded is not null && !EntityContext.InTransaction)
        {
            var revision = held.Revision;
            return await guarded.ConditionalDeleteAsync(successorId,
                Koan.Data.Abstractions.Filtering.LinqFilterCompiler.Compile<JobRecord>(r => r.Revision == revision), ct);
        }
        return await JobRecord.Remove(successorId, ct);
    }

    public async Task<IReadOnlyList<JobRecord>> Held(int page, int limit, CancellationToken ct)
    {
        using var storageScope = JobsStorageScope.Enter();
        if (limit <= 0) return [];
        var oldest = SortBuilder<JobRecord>.Build(s => s.OrderBy(r => r.FirstSubmittedAt));
        return await JobRecord.Query(r => r.Status == JobStatus.Queued && r.HeldBy != null,
            QueryDefinition.All.WithSort(oldest).WithPagination(Math.Max(1, page), limit), ct);
    }

    /// <summary>Settle a queued job as coalesced into <paramref name="intoId"/>, which serves its submission — only while
    /// it is as it was read. False when it changed first.</summary>
    private async Task<bool> Absorb(JobRecord other, string intoId, DateTimeOffset now, CancellationToken ct)
    {
        var read = other.Revision;
        other.ReleaseCoalesceSlot();
        other.ServedBy = intoId;
        other.LastError = $"Coalesced into job {intoId}, which serves this submission.";
        other.LastSettledAt = now;
        other.ExpireAt = _options.ArchiveAfter > TimeSpan.Zero ? now + _options.ArchiveAfter : null;
        other.Transitions.Add(new JobTransition { At = now, From = other.Status, To = JobStatus.Cancelled, Note = $"coalesced into {intoId}" });
        other.Status = JobStatus.Cancelled;

        if (!await ReplaceAtRevision(other, read, ct)) return false;
        Heard(other);   // the submission's waiters move on to the job that serves it
        return true;
    }

    /// <summary>The submissions each open transaction has appended, by coalescing group — invisible to reads until it
    /// commits, so a second submission of a key in the same transaction coalesces onto the first.</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Koan.Data.Core.Transactions.ITransactionCoordinator, System.Collections.Concurrent.ConcurrentDictionary<Guid, JobRecord>> Pending = new();

    private static JobRecord? PendingIn(Koan.Data.Core.Transactions.ITransactionCoordinator? transaction, Guid group)
        => transaction is { IsCompleted: false } && Pending.TryGetValue(transaction, out var pending)
           && pending.TryGetValue(group, out var record)
            ? record
            : null;

    /// <summary>
    /// Claims one job, retrying a claim the store refused for a transient conflict.
    ///
    /// <para>A claim is idempotent by construction: it ends in a conditional write that either wins or reports
    /// that another owner did, so running it again after a rolled-back failure cannot hand one job to two
    /// owners. That property is what makes a retry safe, and it is why this asks
    /// <see cref="DataFailurePolicy.MayRetryIdempotent"/> instead of catching a provider's exception type —
    /// which store errors mean "try again" is the adapter's knowledge, not this ledger's.</para>
    ///
    /// <para>Measured on SQL Server: eight concurrent claimers deadlock about one full suite run in five, and
    /// the victim's exception used to end the drain outright, because nothing above it catches (PMC-056).</para>
    /// </summary>
    public async Task<JobRecord?> ClaimNext(string owner, DateTimeOffset now, DateTimeOffset leaseUntil,
        IReadOnlyCollection<string> saturatedLanes, CancellationToken ct,
        IReadOnlyDictionary<string, PoolDispatchContext>? pools = null)
    {
        using var storageScope = JobsStorageScope.Enter();
        for (var attempt = 1; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return await ClaimOnce(owner, now, leaseUntil, saturatedLanes, ct, pools).ConfigureAwait(false);
            }
            catch (Exception failure) when (
                attempt < ClaimAttempts &&
                DataFailurePolicy.MayRetryIdempotent(failure, ClaimFailureContext(), _classifiers))
            {
                // Nothing committed and the store permits another attempt. Fall through and re-read: the job
                // this claimer wanted may now belong to whoever won the deadlock, which is a normal outcome.
            }
        }
    }

    /// <summary>What a failed claim can truthfully say about itself at the boundary.</summary>
    private static DataFailureContext ClaimFailureContext() => new(
        Data<JobRecord, string>.Capabilities.Owner ?? string.Empty,
        EntityContext.Current?.Source ?? string.Empty,
        "job.claim",
        DataOperationEffect.Write,
        Dispatched: true,
        CommitBoundaryCrossed: true);

    private async Task<JobRecord?> ClaimOnce(string owner, DateTimeOffset now, DateTimeOffset leaseUntil,
        IReadOnlyCollection<string> saturatedLanes, CancellationToken ct,
        IReadOnlyDictionary<string, PoolDispatchContext>? pools = null)
    {
        // JOBS-0010: the §17.2 exclusivity probe reads only the Running rows of the work items it is about to judge, and
        // the JOBS-0007 member tally only the Running rows that hold a pool member — reads bounded by the candidate
        // window and by pool capacity, never the whole Running set.
        var busy = new RunningProbe(now);
        Dictionary<string, int>? memberSlots = null;
        if (pools is { Count: > 0 })
        {
            var members = pools.Values.SelectMany(p => p.Members).Distinct(StringComparer.Ordinal).ToList();
            memberSlots = BuildMemberSlots(pools,
                await JobRecord.Query(r => r.Status == JobStatus.Running && members.Contains(r.GateKey!), ct));
        }
        var claimablePools = memberSlots is null ? null : ClaimablePools(pools!, memberSlots);
        var gatedKeys = (await ActiveGates(now, ct)).Select(g => g.GateKey).ToHashSet(StringComparer.Ordinal);

        // JOBS-0008 lane-fair claim: hydrate each (non-saturated) lane's oldest claimable head — an O(batch) indexed
        // seek per lane (ix_jobs_lane_claim) — then claim in fairness order (weighted fair queuing over per-lane
        // virtual time). The lane universe is the declared lanes (lanes derive from [JobAction]). This replaces the
        // global (VisibleAt, FirstSubmittedAt) scan that let an older / perpetually-fed lane monopolize dispatch, and
        // subsumes the JOBS-0007 forward-paging head-of-line fix: an unclaimable head is paged past WITHIN its own
        // lane's seek, so it can never strand another lane's runnable work.
        // One ordered window read seeds the near-head lanes' heads directly (no per-lane probe) and reveals the lanes
        // present near the head. Lanes whose work is buried beyond the window (a downstream lane behind a deep upstream
        // backlog) come from the declared lane set; each is gated by a cheap Lane-index existence check so an empty lane
        // costs one indexed probe — never an ordered scan over the backlog.
        var batchSize = Math.Max(1, _options.ClaimScanBatch);
        var sort = SortBuilder<JobRecord>.Build(s => s.OrderBy(r => r.VisibleAt).ThenBy(r => r.FirstSubmittedAt));
        // PMC-056 in the pushed-down filter: a fresh reservation is visible only to its named hand; unreserved
        // or lapsed rows stay open to everyone. Kept textual so it translates through every provider's LINQ.
        var window = await JobRecord.Query(
            r => r.Status == JobStatus.Queued && r.VisibleAt <= now && r.CancelRequestedAt == null && r.HeldBy == null
                 && (r.ReservedFor == null || r.ReservedFor == owner || (r.ReservedUntil != null && r.ReservedUntil < now)),
            QueryDefinition.All.WithSort(sort).WithPagination(1, batchSize), ct);

        await busy.Cover(window, ct);
        var heads = new Dictionary<string, JobRecord>(StringComparer.Ordinal);
        var windowLanes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in window)
        {
            windowLanes.Add(r.Lane);
            if (saturatedLanes.Contains(r.Lane) || heads.ContainsKey(r.Lane)) continue;
            if (IsClaimable(r, owner, now, saturatedLanes, claimablePools, gatedKeys, busy)) heads[r.Lane] = r;
        }

        // (a) Window lanes whose window rows were all unclaimable (gated/pool/busy): probe deeper. Bounded by the few
        //     lanes actually present in the window.
        foreach (var lane in windowLanes)
        {
            if (heads.ContainsKey(lane) || saturatedLanes.Contains(lane)) continue;
            var head = await HydrateLaneHead(lane, owner, now, saturatedLanes, claimablePools, gatedKeys, busy, ct);
            if (head is not null) heads[lane] = head;
        }

        // (b) Buried declared lanes (absent from the window — a downstream lane behind a deep upstream backlog): probe
        //     ONLY when a single guard confirms queued work exists outside the window's lanes. A deep single-lane
        //     backlog short-circuits here to ZERO per-lane probes, so the claim stays O(window) instead of O(declared
        //     lanes); when buried work does exist the per-lane seek (index-served on Mongo / a relational composite
        //     index) finds it. JOBS-0008.
        //     The existence guard deliberately ignores reservations: worst case it triggers one extra bounded
        //     per-lane seek that finds nothing claimable, never a missed candidate.
        var buried = _registry.All.SelectMany(b => b.Lanes(_options))
            .Where(l => !windowLanes.Contains(l) && !saturatedLanes.Contains(l))
            .Distinct(StringComparer.Ordinal).ToList();
        if (buried.Count > 0 && await BuriedWorkExists(windowLanes, now, ct))
            foreach (var lane in buried)
            {
                if (heads.ContainsKey(lane)) continue;
                var head = await HydrateLaneHead(lane, owner, now, saturatedLanes, claimablePools, gatedKeys, busy, ct);
                if (head is not null) heads[lane] = head;
            }

        if (heads.Count == 0) return null;

        // §20.3, JOBS-0010: the claim is one write, a compare on the revision the candidate was read at, so a row that
        // changed since — claimed, reserved, cancelled, absorbed — never lands; and it takes the candidate's run slot, so a
        // second job of its key cannot be claimed while one runs. Tried in lane-fair order (WFQ over the per-node virtual
        // time): a loss on the fairest lane's head falls through to the next-fairest lane rather than wasting the poll.
        foreach (var lane in LaneFairSelector.Order(heads.Keys, _virtual))
        {
            var candidate = heads[lane];

            // Pool job: elect a free member and stamp it; skip the lane if none is free (the snapshot may be stale).
            string? elected = null;
            if (candidate.PoolKey is not null)
            {
                if (pools is null || !pools.TryGetValue(candidate.PoolKey, out var ctx)) continue;
                foreach (var member in ctx.Members)
                    if (memberSlots!.GetValueOrDefault(member) < ctx.CapacityPerMember) { elected = member; break; }
                if (elected is null) continue;
                candidate.GateKey = elected;
            }

            // Where the store cannot replace conditionally, the compare is a read just before an ordinary save: the
            // documented at-least-once floor, which still honors a reservation stamped after the candidate was read.
            var read = candidate.Revision;
            Mark(candidate, owner, now, leaseUntil);
            bool landed;
            try
            {
                landed = await ReplaceAtRevision(candidate, read, ct);
            }
            catch (Exception failure) when (!ct.IsCancellationRequested && candidate.CoalesceGroup is not null)
            {
                // Only the run slot's refusal is a lost claim: another job of the item took the slot first. A claim that
                // landed with its answer lost is recognised by its token. Any other failure surfaces exactly as a plain
                // claim's does.
                var stored = await JobRecord.Get(candidate.Id, ct);
                if (stored is not null && string.Equals(stored.ClaimToken, candidate.ClaimToken, StringComparison.Ordinal))
                {
                    landed = true;
                    candidate.Revision = stored.Revision;
                }
                else if (failure is Koan.Data.Abstractions.Failures.UniqueValueConflictException
                         || await KeyRuns(candidate.CoalesceGroup.Value, candidate.Id, ct))
                {
                    landed = false;
                }
                else
                {
                    throw;
                }
            }

            if (landed)
            {
                _virtual[lane] = LaneFairSelector.Charged(_virtual.GetValueOrDefault(lane), _weight(lane));
                if (candidate.CoalesceGroup is not { } group || await ServeKey(candidate, group, now, ct)) return candidate;
                return null;   // the key waits for a deferral that landed meanwhile: the claim went back to the queue
            }

            // CAS loss on a pool job means our slot snapshot is stale — bail so the next drain iteration
            // re-elects with fresh counts; a non-pool loss just falls through to the next-fairest lane.
            if (elected is not null) return null;
        }
        return null;
    }

    public async Task<bool> Update(JobRecord record, CancellationToken ct)
    {
        using var storageScope = JobsStorageScope.Enter();
        if (!await ReplaceAtRevision(record, record.Revision, ct)) return false;
        Heard(record);
        return true;
    }

    /// <summary>Tell the handles waiting in this process that <paramref name="record"/> settled, once its write has landed;
    /// a write deferred to a transaction's commit is read from the ledger instead (JOBS-0010).</summary>
    private void Heard(JobRecord record)
    {
        if (record.IsTerminal && !EntityContext.InTransaction) _completions?.Settled(record);
    }

    /// <summary>The lease merged onto the row as stored, while it carries <paramref name="claimToken"/>: a field another
    /// writer set — a cancellation request — stands, and the owner reads it in the row returned.</summary>
    public async Task<JobRecord?> RenewLease(string jobId, string claimToken, DateTimeOffset leaseUntil, CancellationToken ct)
    {
        using var storageScope = JobsStorageScope.Enter();
        JobRecord? latest = null;
        for (var attempt = 1; attempt <= FenceAttempts; attempt++)
        {
            latest = await JobRecord.Get(jobId, ct);
            if (latest is null || !string.Equals(latest.ClaimToken, claimToken, StringComparison.Ordinal)) return null;
            var row = latest.Clone();
            row.LeaseUntil = leaseUntil;
            try
            {
                if (await ReplaceAtRevision(row, latest.Revision, ct)) return row;
            }
            catch (Exception) when (!ct.IsCancellationRequested && attempt < FenceAttempts)
            {
                // A renewal whose answer was lost still carries the token: the next read writes again.
            }
        }
        return latest;
    }

    /// <summary>One end-of-claim owner write that settles the job (PMC-055, JOBS-0010): a zombie execution, on another node
    /// or this one, carries a token the row no longer holds and writes nothing.</summary>
    public async Task<JobRecord?> TrySettle(JobRecord record, string claimToken, CancellationToken ct)
    {
        using var storageScope = JobsStorageScope.Enter();
        var written = await EndClaim(record, claimToken, ct);
        if (written is not null) Heard(written);
        return written;
    }

    // --- reservation dispatch (PMC-056) ---

    /// <summary>A compare on the revision of the row read just before: applies only while that row is Queued and
    /// unreserved, as it was read.</summary>
    public async Task<bool> TryReserve(string jobId, string hand, DateTimeOffset reservedUntil, DateTimeOffset now, CancellationToken ct)
    {
        using var storageScope = JobsStorageScope.Enter();
        var candidate = await JobRecord.Get(jobId, ct);
        if (candidate is not { Status: JobStatus.Queued } || candidate.ReservedFor != null || candidate.HeldBy != null) return false;
        candidate.ReservedFor = hand;
        candidate.ReservedUntil = reservedUntil;
        return await ReplaceAtRevision(candidate, candidate.Revision, ct);
    }

    public async Task<IReadOnlyList<JobRecord>> ReservationCandidates(DateTimeOffset now, int limit, CancellationToken ct)
    {
        using var storageScope = JobsStorageScope.Enter();
        if (limit <= 0) return [];
        // Pushed down and ordered like the claim head — one bounded indexed seek per dispatch pass.
        var sort = SortBuilder<JobRecord>.Build(s => s.OrderBy(r => r.VisibleAt).ThenBy(r => r.FirstSubmittedAt));
        return await JobRecord.Query(
            r => r.Status == JobStatus.Queued && r.VisibleAt <= now && r.CancelRequestedAt == null && r.ReservedFor == null
                 && r.HeldBy == null,
            QueryDefinition.All.WithSort(sort).WithPagination(1, limit), ct);
    }

    public async Task<IReadOnlyList<JobRecord>> Reservations(CancellationToken ct)
    {
        using var storageScope = JobsStorageScope.Enter();
        return await JobRecord.Query(r => r.Status == JobStatus.Queued && r.ReservedFor != null, ct);
    }

    public async Task Progress(string jobId, string claimToken, double fraction, string? message, CancellationToken ct)
    {
        using var storageScope = JobsStorageScope.Enter();
        for (var attempt = 1; attempt <= FenceAttempts; attempt++)
        {
            var latest = await JobRecord.Get(jobId, ct);
            if (latest is null || !string.Equals(latest.ClaimToken, claimToken, StringComparison.Ordinal)) return;
            var row = latest.Clone();
            row.ProgressFraction = fraction;
            row.ProgressMessage = message;
            try
            {
                if (await ReplaceAtRevision(row, latest.Revision, ct)) return;
            }
            catch (Exception) when (!ct.IsCancellationRequested && attempt < FenceAttempts)
            {
                // The next read tells whether it landed.
            }
        }
    }

    public async Task<IReadOnlyList<JobRecord>> Stuck(DateTimeOffset now, int limit, CancellationToken ct)
    {
        using var storageScope = JobsStorageScope.Enter();
        if (limit <= 0) return [];
        return await JobRecord.Query(
            r => r.Status == JobStatus.Running && r.LeaseUntil != null && r.LeaseUntil < now,
            QueryDefinition.All.WithPagination(1, limit), ct);
    }

    public async Task<IReadOnlyList<JobRecord>> Orphaned(IReadOnlyCollection<string> liveOwners, int limit, CancellationToken ct)
    {
        using var storageScope = JobsStorageScope.Enter();
        if (limit <= 0) return [];
        var live = liveOwners.ToList();
        return await JobRecord.Query(
            r => r.Status == JobStatus.Running && r.Owner != null && !live.Contains(r.Owner),
            QueryDefinition.All.WithPagination(1, limit), ct);
    }

    public async Task<IReadOnlyDictionary<string, int>> RunningLoad(IReadOnlyCollection<string> owners, CancellationToken ct)
    {
        using var storageScope = JobsStorageScope.Enter();
        var hands = owners.ToList();
        var load = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in await JobRecord.Query(r => r.Status == JobStatus.Running && hands.Contains(r.Owner!), ct))
            if (row.Owner is { } owner) load[owner] = load.GetValueOrDefault(owner) + 1;
        return load;
    }

    public async Task<IReadOnlyList<JobRecord>> Query(JobQuery query, CancellationToken ct)
    {
        using var storageScope = JobsStorageScope.Enter();
        var newest = SortBuilder<JobRecord>.Build(s => s.OrderByDescending(r => r.FirstSubmittedAt));
        return await JobRecord.Query(
            JobLedgerPredicates.ForQuery(query),
            QueryDefinition.All.WithSort(newest).WithPagination(query.Page, query.Size), ct);
    }

    /// <summary>One row per gate key (JOBS-0010). The gate is created by an atomic insert and extended by a conditional
    /// replace that applies only while the stored release is earlier, so concurrent backoffs on one key keep the latest
    /// release in one row. Where the store lacks either primitive (SQL Server has no atomic insert today) the row is
    /// written whole and read back, and a writer that reads an earlier release than its own extends again, up to
    /// <see cref="GateAttempts"/> passes. That cannot close the race between two writers creating one gate: when the
    /// earlier release's save lands after the later writer read its own back, the gate keeps the earlier release. Closing
    /// it needs an atomic insert on the store.</summary>
    public async Task SetGate(string gateKey, DateTimeOffset releaseAt, string? reason, CancellationToken ct)
    {
        using var storageScope = JobsStorageScope.Enter();
        var gate = new JobGateRecord { Id = JobGateRecord.IdFor(gateKey), GateKey = gateKey, ReleaseAt = releaseAt, Reason = reason };
        var capabilities = Data<JobGateRecord, string>.Capabilities;
        var conditional = !EntityContext.InTransaction;
        for (var attempt = 0; attempt < GateAttempts; attempt++)
        {
            var existing = await JobGateRecord.Get(gate.Id, ct);
            if (existing is not null && existing.ReleaseAt >= releaseAt) return;
            if (conditional && existing is null && capabilities.Has(DataCaps.Write.InsertOnly))
            {
                if ((await JobGateRecord.Insert(gate, ct: ct)).Outcome != MutationOutcome.Conflict) return;
                continue;   // another node created it first: compare against theirs
            }
            if (conditional && existing is not null && capabilities.Has(DataCaps.Write.ConditionalReplace))
            {
                if (await JobGateRecord.ReplaceIf(gate, g => g.ReleaseAt < releaseAt, ct: ct)) return;
                continue;   // the stored release moved: compare again
            }

            await JobGateRecord.Upsert(gate, ct);
            if (EntityContext.InTransaction) return;   // a deferred write cannot be read back before the commit
            // Read back: a concurrent writer of an earlier release may have overwritten this one; extend again if so.
            if ((await JobGateRecord.Get(gate.Id, ct))?.ReleaseAt >= releaseAt) return;
        }
    }

    public async Task<int> PurgeReleasedGates(DateTimeOffset now, int limit, CancellationToken ct)
    {
        using var storageScope = JobsStorageScope.Enter();
        if (limit <= 0) return 0;
        var released = await JobGateRecord.Query(g => g.ReleaseAt <= now, QueryDefinition.All.WithPagination(1, limit), ct);
        if (released.Count == 0) return 0;
        var ids = released.Select(g => g.Id).ToList();
        // A gate extended between the read and the delete is kept: the delete is guarded where the store can guard it.
        var guarded = Data<JobGateRecord, string>.As<IConditionalDeleteRepository<JobGateRecord, string>>();
        if (guarded is not null)
            return await guarded.ConditionalDeleteMany(ids, Koan.Data.Abstractions.Filtering.LinqFilterCompiler.Compile<JobGateRecord>(g => g.ReleaseAt <= now), ct);

        // No conditional delete (SQL Server today): re-read each gate just before its delete and keep one extended since
        // the page was read. An extension landing between that read and the delete is removed with the gate.
        var removed = 0;
        foreach (var id in ids)
            if ((await JobGateRecord.Get(id, ct)) is { } current && current.ReleaseAt <= now && await JobGateRecord.Remove(id, ct))
                removed++;
        return removed;
    }

    public async Task<IReadOnlyList<JobGateRecord>> ActiveGates(DateTimeOffset now, CancellationToken ct)
    {
        using var storageScope = JobsStorageScope.Enter();
        return await JobGateRecord.Query(g => g.ReleaseAt > now, ct);
    }

    public async Task<int> PurgeArchivable(DateTimeOffset olderThan, int limit, CancellationToken ct)
    {
        using var storageScope = JobsStorageScope.Enter();
        // Pushed down (§19.3) and paged (JOBS-0010): the store returns at most one page of stale benign-terminal rows.
        return await RemovePage(
            r => (r.Status == JobStatus.Completed || r.Status == JobStatus.Cancelled) && r.LastSettledAt < olderThan, limit, ct);
    }

    public async Task<int> PurgeFailed(DateTimeOffset olderThan, int limit, CancellationToken ct)
    {
        using var storageScope = JobsStorageScope.Enter();
        // §19.3 retention completion: Failed/Dead were retained forever; bound them by age (replayable until then).
        return await RemovePage(
            r => (r.Status == JobStatus.Failed || r.Status == JobStatus.Dead) && r.LastSettledAt < olderThan, limit, ct);
    }

    public async Task<int> TrimTerminal(string workType, int keep, int limit, CancellationToken ct)
    {
        using var storageScope = JobsStorageScope.Enter();
        if (keep <= 0 || limit <= 0) return 0;
        // The keep-th newest terminal row marks the cutoff — one row read — and terminal rows settled before it are
        // excess, removed a page at a time.
        var newestFirst = SortBuilder<JobRecord>.Build(s => s.OrderByDescending(r => r.LastSettledAt));
        var boundary = await JobRecord.Query(
            JobLedgerPredicates.TerminalOf(workType), QueryDefinition.All.WithSort(newestFirst).WithPagination(keep, 1), ct);
        if (boundary.Count == 0 || boundary[0].LastSettledAt is not { } cutoff) return 0;   // under the cap
        return await RemovePage(
            JobLedgerPredicates.And(JobLedgerPredicates.TerminalOf(workType), r => r.LastSettledAt < cutoff), limit, ct);
    }

    /// <summary>Read at most <paramref name="limit"/> rows matching <paramref name="predicate"/> and remove them.</summary>
    private static async Task<int> RemovePage(Expression<Func<JobRecord, bool>> predicate, int limit, CancellationToken ct)
    {
        if (limit <= 0) return 0;
        var page = await JobRecord.Query(predicate, QueryDefinition.All.WithPagination(1, limit), ct);
        return page.Count == 0 ? 0 : await JobRecord.Remove(page.Select(r => r.Id).ToList(), ct);
    }

    public async Task<long> CountActive(string workType, CancellationToken ct)
    {
        using var storageScope = JobsStorageScope.Enter();
        return (await JobRecord.QueryWithCount(
            JobLedgerPredicates.ActiveOf(workType),
            QueryDefinition.All.WithPagination(1, 1), ct)).TotalCount;
    }

    public async Task<JobsHealthSnapshot> HealthSnapshot(DateTimeOffset now, CancellationToken ct)
    {
        using var storageScope = JobsStorageScope.Enter();
        // Cheap + index-served: pushed COUNTs over the single-column Status index + one LIMIT-1 ordered oldest-due seek.
        // No per-lane fan-out — a health probe must not become a scan-storm over the backlog it is meant to observe.
        var queued = (await JobRecord.QueryWithCount(r => r.Status == JobStatus.Queued, QueryDefinition.All.WithPagination(1, 1), ct)).TotalCount;
        var running = (await JobRecord.QueryWithCount(r => r.Status == JobStatus.Running, QueryDefinition.All.WithPagination(1, 1), ct)).TotalCount;
        var reclaim = (await JobRecord.QueryWithCount(
            r => r.Status == JobStatus.Running && r.LeaseUntil != null && r.LeaseUntil < now,
            QueryDefinition.All.WithPagination(1, 1), ct)).TotalCount;

        var age = TimeSpan.Zero;
        if (queued > 0)
        {
            var oldestSort = SortBuilder<JobRecord>.Build(s => s.OrderBy(r => r.VisibleAt).ThenBy(r => r.FirstSubmittedAt));
            var head = await JobRecord.Query(
                r => r.Status == JobStatus.Queued && r.VisibleAt <= now,
                QueryDefinition.All.WithSort(oldestSort).WithPagination(1, 1), ct);
            if (head.Count > 0) { var d = now - head[0].VisibleAt; if (d > TimeSpan.Zero) age = d; }
        }
        return new JobsHealthSnapshot(queued, running, reclaim, age);
    }

    // --- claim internals ---

    /// <summary>The rows that can hold a candidate back, read once per batch of candidates and only for what was not read
    /// yet (JOBS-0010): the Running rows of the work items a claim is about to judge (§17.2), and, for a candidate with a
    /// coalesce key, the key's running job — which holds its run slot — and its queued jobs deferred past now, whose
    /// deferral the key's work waits for.</summary>
    private sealed class RunningProbe(DateTimeOffset now)
    {
        private readonly HashSet<string> _read = new(StringComparer.Ordinal);
        private readonly HashSet<Guid> _keysRead = new();

        public HashSet<(string Type, string Id, JobDataRoute Route)> Busy { get; } = new();

        /// <summary>The coalesce keys whose jobs cannot be claimed now.</summary>
        public HashSet<Guid> HeldKeys { get; } = new();

        public async Task Cover(IReadOnlyCollection<JobRecord> candidates, CancellationToken ct)
        {
            var workIds = candidates.Where(r => r.Exclusive).Select(r => r.WorkId).Where(_read.Add).ToList();
            if (workIds.Count > 0)
                foreach (var row in await JobRecord.Query(r => r.Status == JobStatus.Running && workIds.Contains(r.WorkId), ct))
                    Busy.Add(JobDataRoute.Identity(row));

            var groups = candidates.Select(r => r.CoalesceGroup).OfType<Guid>().Where(_keysRead.Add).Select(g => (Guid?)g).ToList();
            if (groups.Count == 0) return;
            var at = now;
            foreach (var row in await JobRecord.Query(
                         r => groups.Contains(r.CoalesceGroup)
                              && (r.Status == JobStatus.Running
                                  || (r.Status == JobStatus.Queued && r.VisibleAt > at && r.CancelRequestedAt == null
                                      && r.HeldBy == null)), ct))
                if (row.CoalesceGroup is { } held) HeldKeys.Add(held);
        }
    }

    private static Dictionary<string, int> BuildMemberSlots(
        IReadOnlyDictionary<string, PoolDispatchContext> pools, IReadOnlyList<JobRecord> running)
    {
        var slots = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var ctx in pools.Values)
            foreach (var m in ctx.Members)
                slots.TryAdd(m, 0);
        foreach (var r in running)
            if (r.GateKey is not null && slots.ContainsKey(r.GateKey))
                slots[r.GateKey]++;
        return slots;
    }

    /// <summary>Pool names that currently have at least one member with open capacity. A queued pool job is claimable
    /// only if its <see cref="JobRecord.PoolKey"/> is in this set; excluding the rest at selection time keeps an
    /// exhausted (or unresolvable) pool's backlog from consuming the claim-scan window (JOBS-0007 head-of-line bug).</summary>
    private static HashSet<string> ClaimablePools(
        IReadOnlyDictionary<string, PoolDispatchContext> pools, Dictionary<string, int> memberSlots)
    {
        var claimable = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, ctx) in pools)
            foreach (var m in ctx.Members)
                if (memberSlots.GetValueOrDefault(m) < ctx.CapacityPerMember) { claimable.Add(name); break; }
        return claimable;
    }

    /// <summary>One bounded guard (JOBS-0008): does any claimable queued row exist in a lane NOT already covered by the
    /// window read? If not, there are no buried lanes to probe and the per-lane fan-out is skipped entirely (the deep
    /// single-lane backlog case). One LIMIT-1 read, not one-per-declared-lane.</summary>
    private static async Task<bool> BuriedWorkExists(HashSet<string> windowLanes, DateTimeOffset now, CancellationToken ct)
        => (await JobRecord.Query(
            r => r.Status == JobStatus.Queued && r.VisibleAt <= now && r.CancelRequestedAt == null && r.HeldBy == null
                 && !windowLanes.Contains(r.Lane),
            QueryDefinition.All.WithPagination(1, 1), ct)).Count > 0;

    /// <summary>The per-lane head seek (ix_jobs_lane_claim): the oldest claimable Queued+due row in <paramref name="lane"/>.
    /// Pages forward past this lane's own unclaimable head (gated / pool-exhausted / busy-exclusive / reserved elsewhere)
    /// to its oldest claimable row — O(batch) and index-served, never O(backlog). Returns null when the lane has no
    /// claimable work.</summary>
    private async Task<JobRecord?> HydrateLaneHead(
        string lane, string owner, DateTimeOffset now, IReadOnlyCollection<string> saturatedLanes,
        HashSet<string>? claimablePools, HashSet<string> gatedKeys,
        RunningProbe busy, CancellationToken ct)
    {
        var batchSize = Math.Max(1, _options.ClaimScanBatch);
        var sort = SortBuilder<JobRecord>.Build(s => s.OrderBy(r => r.VisibleAt).ThenBy(r => r.FirstSubmittedAt));
        for (var page = 1; ; page++)
        {
            var scan = QueryDefinition.All.WithSort(sort).WithPagination(page, batchSize);
            var window = await JobRecord.Query(
                r => r.Status == JobStatus.Queued && r.Lane == lane && r.VisibleAt <= now && r.CancelRequestedAt == null
                     && r.HeldBy == null,
                scan, ct);
            if (window.Count == 0) return null;
            await busy.Cover(window, ct);
            foreach (var r in window)
                if (IsClaimable(r, owner, now, saturatedLanes, claimablePools, gatedKeys, busy)) return r;
            if (window.Count < batchSize) return null;   // short page → this lane's ready set is exhausted
        }
    }

    /// <summary>A queued row is claimable by <paramref name="owner"/> now iff it is open to that hand (PMC-056:
    /// unreserved / reserved for them / reservation lapsed), its lane isn't saturated, it isn't an exclusive job whose
    /// work-item is already running, and — pool-XOR-gate — a pool job's pool currently has an open member while a
    /// non-pool job's gate (if any) isn't active. <paramref name="claimablePools"/> null means no pool has capacity (or no
    /// resolver), so every pool job is currently unclaimable. The pool-XOR-gate split matches <see cref="InMemoryJobLedger"/>
    /// so the tiers converge (ARCH-0079): a pool job's admission is governed by member capacity, not by resource gates —
    /// its <see cref="JobRecord.GateKey"/> is the elected member (null while queued), not a cooperative-backoff key. A job
    /// whose coalesce key runs, or waits for a deferral, is not claimable either (JOBS-0010).</summary>
    private static bool IsClaimable(JobRecord r, string owner, DateTimeOffset now,
        IReadOnlyCollection<string> saturatedLanes, HashSet<string>? claimablePools,
        HashSet<string> gatedKeys, RunningProbe probe)
    {
        if (!(r.ReservedFor == null || r.ReservedFor == owner || (r.ReservedUntil != null && r.ReservedUntil < now)))
            return false;
        if (saturatedLanes.Contains(r.Lane)) return false;
        if (r.Exclusive && probe.Busy.Contains(JobDataRoute.Identity(r))) return false;
        if (r.CoalesceGroup is { } key && probe.HeldKeys.Contains(key)) return false;
        if (r.PoolKey is not null)
            return claimablePools is not null && claimablePools.Contains(r.PoolKey);
        return r.GateKey is null || !gatedKeys.Contains(r.GateKey);
    }

    private static void Mark(JobRecord r, string owner, DateTimeOffset now, DateTimeOffset leaseUntil)
    {
        r.Attempt++;
        r.ClaimToken = Guid.NewGuid().ToString("N");   // this claim's token: every write of its owner is fenced on it
        r.ReleaseCoalesceSlot();   // a claimed job no longer absorbs submissions (JOBS-0006 trailing edge)
        r.TakeRunSlot();           // and no other job of its key runs beside it (JOBS-0010)
        r.Transitions.Add(new JobTransition { At = now, From = r.Status, To = JobStatus.Running, Note = $"claimed by {owner}" });
        r.Status = JobStatus.Running;
        r.Owner = owner;
        r.LeaseUntil = leaseUntil;
        // PMC-056: the claim consumes the reservation cookie (if this hand held one).
        r.ReservedFor = null;
        r.ReservedUntil = null;
    }

}
