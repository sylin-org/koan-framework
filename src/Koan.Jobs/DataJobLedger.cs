using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Core;
using Koan.Data.Core.Sorting;
using Microsoft.Extensions.Options;

namespace Koan.Jobs;

/// <summary>
/// Durable <see cref="IJobLedger"/> riding the existing data layer — the ledger and gates are each
/// an <see cref="Koan.Data.Core.Model.Entity{T}"/> persisted via the ambient adapter, so there are no per-DB job
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

    public DataJobLedger(IOptions<JobsOptions> options, JobTypeRegistry registry)
    {
        _options = options.Value;
        _registry = registry;
        _weight = lane => _options.LaneWeights.GetValueOrDefault(lane, 1.0);
    }

    public Task Append(JobRecord record, CancellationToken ct) => JobRecord.Upsert(record, ct);

    public async Task AppendMany(IReadOnlyCollection<JobRecord> records, CancellationToken ct)
        => await JobRecord.UpsertMany(records, ct);

    public Task<JobRecord?> Get(string jobId, CancellationToken ct) => JobRecord.Get(jobId, ct);

    public async Task<JobRecord?> FindActiveByCoalesceKey(string workType, string coalesceKey, CancellationToken ct)
    {
        // Queued-only: a Running job does not block a new submit — the submit queues a trailing execution
        // (at most 1 running + 1 queued per coalesce key, the debounce / trailing-edge pattern).
        var hits = await JobRecord.Query(r => r.WorkType == workType && r.CoalesceKey == coalesceKey && r.Status == JobStatus.Queued, ct);
        return hits.FirstOrDefault();
    }

    public async Task<JobRecord?> ClaimNext(string owner, DateTimeOffset now, DateTimeOffset leaseUntil,
        IReadOnlyCollection<string> saturatedLanes, CancellationToken ct,
        IReadOnlyDictionary<string, PoolDispatchContext>? pools = null)
    {
        // One Running snapshot serves both the §17.2 exclusivity probe and the pool member-slot tally (JOBS-0007).
        var running = await JobRecord.Query(r => r.Status == JobStatus.Running, ct);
        var busy = running.Select(r => (r.WorkType, r.WorkId)).ToHashSet();
        var memberSlots = pools is { Count: > 0 } ? BuildMemberSlots(pools, running) : null;
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
        var window = await JobRecord.Query(
            r => r.Status == JobStatus.Queued && r.VisibleAt <= now && r.CancelRequestedAt == null,
            QueryDefinition.All.WithSort(sort).WithPagination(1, batchSize), ct);

        var heads = new Dictionary<string, JobRecord>(StringComparer.Ordinal);
        var windowLanes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in window)
        {
            windowLanes.Add(r.Lane);
            if (saturatedLanes.Contains(r.Lane) || heads.ContainsKey(r.Lane)) continue;
            if (IsClaimable(r, saturatedLanes, claimablePools, gatedKeys, busy)) heads[r.Lane] = r;
        }

        // (a) Window lanes whose window rows were all unclaimable (gated/pool/busy): probe deeper. Bounded by the few
        //     lanes actually present in the window.
        foreach (var lane in windowLanes)
        {
            if (heads.ContainsKey(lane) || saturatedLanes.Contains(lane)) continue;
            var head = await HydrateLaneHead(lane, now, saturatedLanes, claimablePools, gatedKeys, busy, ct);
            if (head is not null) heads[lane] = head;
        }

        // (b) Buried declared lanes (absent from the window — a downstream lane behind a deep upstream backlog): probe
        //     ONLY when a single guard confirms queued work exists outside the window's lanes. A deep single-lane
        //     backlog short-circuits here to ZERO per-lane probes, so the claim stays O(window) instead of O(declared
        //     lanes); when buried work does exist the per-lane seek (index-served on Mongo / a relational composite
        //     index) finds it. JOBS-0008.
        var buried = _registry.All.SelectMany(b => b.Lanes(_options))
            .Where(l => !windowLanes.Contains(l) && !saturatedLanes.Contains(l))
            .Distinct(StringComparer.Ordinal).ToList();
        if (buried.Count > 0 && await BuriedWorkExists(windowLanes, now, ct))
            foreach (var lane in buried)
            {
                if (heads.ContainsKey(lane)) continue;
                var head = await HydrateLaneHead(lane, now, saturatedLanes, claimablePools, gatedKeys, busy, ct);
                if (head is not null) heads[lane] = head;
            }

        if (heads.Count == 0) return null;

        // §20.3 capability-graded claim, tried in lane-fair order (WFQ over the per-node virtual time): a CAS-loss on
        // the fairest lane's head falls through to the next-fairest lane rather than wasting the poll.
        var cas = Data<JobRecord, string>.Capabilities.Has(DataCaps.Write.ConditionalReplace)
            ? Data<JobRecord, string>.As<IConditionalWriteRepository<JobRecord, string>>()
            : null;
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

            JobRecord? claimed;
            if (cas is null)
            {
                claimed = await ClaimOptimistic(candidate, owner, now, leaseUntil, ct);   // GateKey already stamped above
            }
            else
            {
                Mark(candidate, owner, now, leaseUntil);
                claimed = await cas.ConditionalReplaceAsync(candidate, r => r.Status == JobStatus.Queued && r.Owner == null, ct)
                    ? candidate : null;
            }

            if (claimed is not null) { _virtual[lane] = LaneFairSelector.Charged(_virtual.GetValueOrDefault(lane), _weight(lane)); return claimed; }

            // CAS loss on a pool job means our slot snapshot is stale — bail so the next drain iteration
            // re-elects with fresh counts; a non-pool loss just falls through to the next-fairest lane.
            if (elected is not null) return null;
        }
        return null;
    }

    public Task Update(JobRecord record, CancellationToken ct) => JobRecord.Upsert(record, ct);

    public async Task Progress(string jobId, double fraction, string? message, CancellationToken ct)
    {
        var r = await JobRecord.Get(jobId, ct);
        if (r is null) return;
        r.ProgressFraction = fraction;
        r.ProgressMessage = message;
        await JobRecord.Upsert(r, ct);
    }

    public async Task<IReadOnlyList<JobRecord>> Stuck(DateTimeOffset now, CancellationToken ct)
    {
        var running = await JobRecord.Query(r => r.Status == JobStatus.Running, ct);
        return running.Where(r => r.LeaseUntil is { } l && l < now).ToList();
    }

    public Task<IReadOnlyList<JobRecord>> NonTerminal(CancellationToken ct)
        // Pushed down (§19.3): Status < Completed — terminals are 4..7, so one comparison, not All() + in-memory filter.
        => JobRecord.Query(JobLedgerPredicates.NonTerminal(), ct);

    public Task<IReadOnlyList<JobRecord>> InStage(string workType, string action, CancellationToken ct)
        // Pushed down (§19.3): the full (WorkType, Action, Status==Queued) predicate, not WorkType + in-memory filter.
        => JobRecord.Query(r => r.WorkType == workType && r.Action == action && r.Status == JobStatus.Queued, ct);

    public Task<IReadOnlyList<JobRecord>> Query(JobQuery query, CancellationToken ct)
        // Pushed down (§19.3): the declarative facade query becomes a tight conjunctive predicate the store evaluates,
        // so WithStatus(s) returns only the matching rows — not every JobRecord of the work-type.
        => JobRecord.Query(JobLedgerPredicates.ForQuery(query), ct);

    public async Task SetGate(string gateKey, DateTimeOffset releaseAt, string? reason, CancellationToken ct)
    {
        var existing = (await JobGateRecord.Query(g => g.GateKey == gateKey, ct)).FirstOrDefault();
        if (existing is not null)
        {
            if (existing.ReleaseAt >= releaseAt) return;
            existing.ReleaseAt = releaseAt;
            existing.Reason = reason;
            await JobGateRecord.Upsert(existing, ct);
            return;
        }
        await JobGateRecord.Upsert(new JobGateRecord { GateKey = gateKey, ReleaseAt = releaseAt, Reason = reason }, ct);
    }

    public async Task<IReadOnlyList<JobGate>> ActiveGates(DateTimeOffset now, CancellationToken ct)
    {
        var all = await JobGateRecord.All(ct);
        return all.Where(g => g.ReleaseAt > now)
            .Select(g => new JobGate { GateKey = g.GateKey, ReleaseAt = g.ReleaseAt, Reason = g.Reason })
            .ToList();
    }

    public async Task<int> PurgeArchivable(DateTimeOffset olderThan, CancellationToken ct)
    {
        // Pushed down (§19.3): the LastSettledAt cutoff is in the predicate, so the store returns only the stale
        // benign-terminal rows — not every Completed/Cancelled row to be filtered in memory.
        var stale = await JobRecord.Query(
            r => (r.Status == JobStatus.Completed || r.Status == JobStatus.Cancelled) && r.LastSettledAt < olderThan, ct);
        foreach (var r in stale) await JobRecord.Remove(r.Id, ct);
        return stale.Count;
    }

    public async Task<int> PurgeFailed(DateTimeOffset olderThan, CancellationToken ct)
    {
        // §19.3 retention completion: Failed/Dead were retained forever; bound them by age (replayable until then).
        var stale = await JobRecord.Query(
            r => (r.Status == JobStatus.Failed || r.Status == JobStatus.Dead) && r.LastSettledAt < olderThan, ct);
        foreach (var r in stale) await JobRecord.Remove(r.Id, ct);
        return stale.Count;
    }

    public async Task<int> TrimTerminal(string workType, int keep, CancellationToken ct)
    {
        if (keep <= 0) return 0;
        // The keep-th newest terminal row marks the cutoff; terminal rows settled before it are excess. Two pushed
        // queries (a bounded page to find the cutoff, then a predicate delete) — no full materialize of the work-type.
        var sort = SortBuilder<JobRecord>.Build(s => s.OrderByDescending(r => r.LastSettledAt));
        var newestPage = QueryDefinition.All.WithSort(sort).WithPagination(1, keep);
        var newest = await JobRecord.Query(JobLedgerPredicates.TerminalOf(workType), newestPage, ct);
        if (newest.Count < keep) return 0;                         // under the cap — nothing to trim
        if (newest[^1].LastSettledAt is not { } cutoff) return 0;
        var excess = await JobRecord.Query(
            JobLedgerPredicates.And(JobLedgerPredicates.TerminalOf(workType), r => r.LastSettledAt < cutoff), ct);
        foreach (var r in excess) await JobRecord.Remove(r.Id, ct);
        return excess.Count;
    }

    public async Task<long> CountActive(string workType, CancellationToken ct)
        // Pushed COUNT (one row materialized): cheap enough to run per work-type each archival sweep.
        => (await JobRecord.QueryWithCount(
            JobLedgerPredicates.ActiveOf(workType),
            QueryDefinition.All.WithPagination(1, 1), ct)).TotalCount;

    public async Task<JobsHealthSnapshot> HealthSnapshot(DateTimeOffset now, CancellationToken ct)
    {
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
            r => r.Status == JobStatus.Queued && r.VisibleAt <= now && r.CancelRequestedAt == null && !windowLanes.Contains(r.Lane),
            QueryDefinition.All.WithPagination(1, 1), ct)).Count > 0;

    /// <summary>The per-lane head seek (ix_jobs_lane_claim): the oldest claimable Queued+due row in <paramref name="lane"/>.
    /// Pages forward past this lane's own unclaimable head (gated / pool-exhausted / busy-exclusive) to its oldest
    /// claimable row — O(batch) and index-served, never O(backlog). Returns null when the lane has no claimable work.</summary>
    private async Task<JobRecord?> HydrateLaneHead(
        string lane, DateTimeOffset now, IReadOnlyCollection<string> saturatedLanes,
        HashSet<string>? claimablePools, HashSet<string> gatedKeys,
        HashSet<(string WorkType, string WorkId)> busy, CancellationToken ct)
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
            foreach (var r in window)
                if (IsClaimable(r, saturatedLanes, claimablePools, gatedKeys, busy)) return r;
            if (window.Count < batchSize) return null;   // short page → this lane's ready set is exhausted
        }
    }

    /// <summary>A queued row is claimable now iff its lane isn't saturated, it isn't an exclusive job whose work-item is
    /// already running, and — pool-XOR-gate — a pool job's pool currently has an open member while a non-pool job's gate
    /// (if any) isn't active. <paramref name="claimablePools"/> null means no pool has capacity (or no resolver), so
    /// every pool job is currently unclaimable. The pool-XOR-gate split matches <see cref="InMemoryJobLedger"/> so the
    /// tiers converge (ARCH-0079): a pool job's admission is governed by member capacity, not by resource gates — its
    /// <see cref="JobRecord.GateKey"/> is the elected member (null while queued), not a cooperative-backoff key.</summary>
    private static bool IsClaimable(JobRecord r,
        IReadOnlyCollection<string> saturatedLanes, HashSet<string>? claimablePools,
        HashSet<string> gatedKeys, HashSet<(string WorkType, string WorkId)> busy)
    {
        if (saturatedLanes.Contains(r.Lane)) return false;
        if (r.Exclusive && busy.Contains((r.WorkType, r.WorkId))) return false;
        if (r.PoolKey is not null)
            return claimablePools is not null && claimablePools.Contains(r.PoolKey);
        return r.GateKey is null || !gatedKeys.Contains(r.GateKey);
    }

    private static async Task<JobRecord?> ClaimOptimistic(JobRecord candidate, string owner, DateTimeOffset now, DateTimeOffset leaseUntil, CancellationToken ct)
    {
        Mark(candidate, owner, now, leaseUntil);
        await JobRecord.Upsert(candidate, ct);
        return candidate;
    }

    private static void Mark(JobRecord r, string owner, DateTimeOffset now, DateTimeOffset leaseUntil)
    {
        r.Attempt++;
        r.Transitions.Add(new JobTransition { At = now, From = r.Status, To = JobStatus.Running, Note = $"claimed by {owner}" });
        r.Status = JobStatus.Running;
        r.Owner = owner;
        r.LeaseUntil = leaseUntil;
    }

}
