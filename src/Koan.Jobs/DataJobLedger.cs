using System.Linq.Expressions;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Abstractions.Failures;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Core;
using Koan.Data.Core.Failures;
using Koan.Data.Core.Sorting;
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

    /// <summary>Attempts one claim gets when the store reports a transient conflict. Two is enough for a
    /// deadlock — the winner has committed by the time the victim rolls back — and the third is headroom for a
    /// second collision under contention rather than a policy of grinding.</summary>
    private const int ClaimAttempts = 3;

    public DataJobLedger(
        IOptions<JobsOptions> options,
        JobTypeRegistry registry,
        IEnumerable<IDataFailureClassifier>? classifiers = null,
        JobCompletions? completions = null)
    {
        _completions = completions;
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
        return await FirstQueuedOf(group, ct) ?? PendingIn(EntityContext.CurrentTransaction, group);
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
            // on one: it coalesces onto a committed job of its key or onto one this transaction already submitted.
            if ((await FirstQueuedOf(group, ct) ?? PendingIn(transaction, group)) is { } queued) return queued;
            await Append(record, ct);
            Pending.GetOrCreateValue(transaction)[group] = record;
            return null;
        }

        if (await FirstQueuedOf(group, ct) is { } holder) return holder;
        record.CoalesceSlot = group;
        try
        {
            await Append(record, ct);
            return null;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // Another submission took the slot first: coalesce onto it. Its holder may already have been claimed, or the
            // store refused the write for another reason, which appending without the slot surfaces.
            record.ReleaseCoalesceSlot();
            if (await FirstQueuedOf(group, ct) is { } taken) return taken;
            await Append(record, ct);
            return null;
        }
    }

    /// <summary>A queued, uncancelled job of a coalescing group: one indexed read.</summary>
    private static async Task<JobRecord?> FirstQueuedOf(Guid group, CancellationToken ct)
        => (await JobRecord.Query(
            r => r.CoalesceGroup == group && r.Status == JobStatus.Queued && r.CancelRequestedAt == null,
            QueryDefinition.All.WithPagination(1, 1), ct)).FirstOrDefault();

    /// <summary>Write <paramref name="record"/> over the stored row only while that row still carries <paramref name="read"/>,
    /// the revision its writer read (JOBS-0010); the record takes a fresh revision. Where the store replaces conditionally
    /// and no transaction defers the write, that is one conditional replace, so a stale copy never lands. Elsewhere the
    /// stored revision is read and compared just before an ordinary save: the at-least-once floor, where a write landing
    /// between that read and the save is overwritten.</summary>
    private static async Task<bool> ReplaceAtRevision(JobRecord record, Guid read, CancellationToken ct)
    {
        record.Revision = Guid.NewGuid();
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
        var busy = new RunningProbe();
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
            r => r.Status == JobStatus.Queued && r.VisibleAt <= now && r.CancelRequestedAt == null
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
        // changed since — claimed, reserved, cancelled — never lands. Tried in lane-fair order (WFQ over the per-node
        // virtual time): a loss on the fairest lane's head falls through to the next-fairest lane rather than wasting the
        // poll.
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
            if (await ReplaceAtRevision(candidate, read, ct))
            {
                _virtual[lane] = LaneFairSelector.Charged(_virtual.GetValueOrDefault(lane), _weight(lane));
                return candidate;
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
        // A write deferred to a transaction's commit is read from the ledger instead.
        if (record.IsTerminal && !EntityContext.InTransaction) _completions?.Settled(record);
        return true;
    }

    // --- reservation dispatch (PMC-056) ---

    /// <summary>A compare on the revision of the row read just before: applies only while that row is Queued and
    /// unreserved, as it was read.</summary>
    public async Task<bool> TryReserve(string jobId, string hand, DateTimeOffset reservedUntil, DateTimeOffset now, CancellationToken ct)
    {
        using var storageScope = JobsStorageScope.Enter();
        var candidate = await JobRecord.Get(jobId, ct);
        if (candidate is not { Status: JobStatus.Queued } || candidate.ReservedFor != null) return false;
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
            r => r.Status == JobStatus.Queued && r.VisibleAt <= now && r.CancelRequestedAt == null && r.ReservedFor == null,
            QueryDefinition.All.WithSort(sort).WithPagination(1, limit), ct);
    }

    public async Task<IReadOnlyList<JobRecord>> Reservations(CancellationToken ct)
    {
        using var storageScope = JobsStorageScope.Enter();
        return await JobRecord.Query(r => r.Status == JobStatus.Queued && r.ReservedFor != null, ct);
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

    /// <summary>One row per gate key (JOBS-0010), written whole unless the stored release is already as late.</summary>
    public async Task SetGate(string gateKey, DateTimeOffset releaseAt, string? reason, CancellationToken ct)
    {
        using var storageScope = JobsStorageScope.Enter();
        var id = JobGateRecord.IdFor(gateKey);
        if (await JobGateRecord.Get(id, ct) is { } existing && existing.ReleaseAt >= releaseAt) return;
        await JobGateRecord.Upsert(new JobGateRecord { Id = id, GateKey = gateKey, ReleaseAt = releaseAt, Reason = reason }, ct);
    }

    public async Task<int> PurgeReleasedGates(DateTimeOffset now, int limit, CancellationToken ct)
    {
        using var storageScope = JobsStorageScope.Enter();
        if (limit <= 0) return 0;
        var released = await JobGateRecord.Query(g => g.ReleaseAt <= now, QueryDefinition.All.WithPagination(1, limit), ct);
        return released.Count == 0 ? 0 : await JobGateRecord.Remove(released.Select(g => g.Id).ToList(), ct);
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

    /// <summary>The Running rows of the work items a claim is about to judge (§17.2), read once per batch of candidates and
    /// only for what was not read yet (JOBS-0010).</summary>
    private sealed class RunningProbe
    {
        private readonly HashSet<string> _read = new(StringComparer.Ordinal);

        public HashSet<(string Type, string Id, JobDataRoute Route)> Busy { get; } = new();

        public async Task Cover(IReadOnlyCollection<JobRecord> candidates, CancellationToken ct)
        {
            var workIds = candidates.Where(r => r.Exclusive).Select(r => r.WorkId).Where(_read.Add).ToList();
            if (workIds.Count == 0) return;
            foreach (var row in await JobRecord.Query(r => r.Status == JobStatus.Running && workIds.Contains(r.WorkId), ct))
                Busy.Add(JobDataRoute.Identity(row));
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
            r => r.Status == JobStatus.Queued && r.VisibleAt <= now && r.CancelRequestedAt == null
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
                r => r.Status == JobStatus.Queued && r.Lane == lane && r.VisibleAt <= now && r.CancelRequestedAt == null,
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
    /// its <see cref="JobRecord.GateKey"/> is the elected member (null while queued), not a cooperative-backoff key.</summary>
    private static bool IsClaimable(JobRecord r, string owner, DateTimeOffset now,
        IReadOnlyCollection<string> saturatedLanes, HashSet<string>? claimablePools,
        HashSet<string> gatedKeys, RunningProbe probe)
    {
        if (!(r.ReservedFor == null || r.ReservedFor == owner || (r.ReservedUntil != null && r.ReservedUntil < now)))
            return false;
        if (saturatedLanes.Contains(r.Lane)) return false;
        if (r.Exclusive && probe.Busy.Contains(JobDataRoute.Identity(r))) return false;
        if (r.PoolKey is not null)
            return claimablePools is not null && claimablePools.Contains(r.PoolKey);
        return r.GateKey is null || !gatedKeys.Contains(r.GateKey);
    }

    private static void Mark(JobRecord r, string owner, DateTimeOffset now, DateTimeOffset leaseUntil)
    {
        r.Attempt++;
        r.ReleaseCoalesceSlot();   // a claimed job no longer takes submissions (JOBS-0006 trailing edge)
        r.Transitions.Add(new JobTransition { At = now, From = r.Status, To = JobStatus.Running, Note = $"claimed by {owner}" });
        r.Status = JobStatus.Running;
        r.Owner = owner;
        r.LeaseUntil = leaseUntil;
        // PMC-056: the claim consumes the reservation cookie (if this hand held one).
        r.ReservedFor = null;
        r.ReservedUntil = null;
    }
}
