using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Core;
using Koan.Data.Core.Sorting;
using Microsoft.Extensions.Options;

namespace Koan.Jobs;

/// <summary>
/// Durable <see cref="IJobLedger"/> riding the existing data layer — the ledger, gates, and claim tickets are each
/// an <see cref="Koan.Data.Core.Model.Entity{T}"/> persisted via the ambient adapter, so there are no per-DB job
/// adapters and durability follows whatever data provider is present (JOBS-0005 §7/§8). The claim is
/// strategy-graded: <see cref="ClaimStrategy.Optimistic"/> (last-write-wins, idempotency backstop) or
/// <see cref="ClaimStrategy.Ticket"/> (the leaderless GUIDv7 bakery election over a parallel ticket set).
/// </summary>
public sealed class DataJobLedger : IJobLedger
{
    private readonly TimeProvider _clock;
    private readonly JobsOptions _options;

    public DataJobLedger(TimeProvider clock, IOptions<JobsOptions> options)
    {
        _clock = clock;
        _options = options.Value;
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
        // One Running snapshot serves both the §17.2 exclusivity probe and the pool member-slot tally (JOBS-0007),
        // and it is taken BEFORE candidate selection: knowing which pools currently have an open member lets the scan
        // exclude an exhausted pool's queued jobs at selection time. Otherwise a backlog of unclaimable pool jobs at
        // the FIFO head consumes the whole scan window and starves runnable work behind it (the head-of-line bug).
        var running = await JobRecord.Query(r => r.Status == JobStatus.Running, ct);
        var busy = running.Select(r => (r.WorkType, r.WorkId)).ToHashSet();

        // Member slot counts (when pool jobs are in play) + the set of pools that currently have an open member.
        var memberSlots = pools is { Count: > 0 } ? BuildMemberSlots(pools, running) : null;
        var claimablePools = memberSlots is null ? null : ClaimablePools(pools!, memberSlots);

        var candidates = await SelectCandidates(now, saturatedLanes, claimablePools, busy, ct);
        if (candidates.Count == 0) return null;

        // Ticket strategy runs its own leaderless election; claim the FIFO head through it.
        if (_options.ClaimStrategy == ClaimStrategy.Ticket)
        {
            var (ticketTarget, ticketMember) = ElectTarget(candidates, pools, memberSlots);
            if (ticketTarget is null) return null;
            return await ClaimViaTicket(ticketTarget, owner, now, leaseUntil, ticketMember, ct);
        }

        // §20.3 capability-graded claim. On a store with atomic conditional replace, mark Running via a compare-and-set
        // (apply IFF still Queued && unowned) — contention-free and single-execution: concurrent claimers can't both win,
        // and a CAS-loss just falls through to the next runnable candidate. Adapters without the capability fall back to
        // the last-write-wins optimistic mark (idempotency remains the backstop there).
        var cas = Data<JobRecord, string>.Capabilities.Has(DataCaps.Write.ConditionalReplace)
            ? Data<JobRecord, string>.As<IConditionalWriteRepository<JobRecord, string>>()
            : null;
        if (cas is null)
        {
            var (optTarget, optMember) = ElectTarget(candidates, pools, memberSlots);
            if (optTarget is null) return null;
            if (optMember is not null) optTarget.GateKey = optMember;
            return await ClaimOptimistic(optTarget, owner, now, leaseUntil, ct);
        }

        foreach (var candidate in candidates)
        {
            string? elected = null;
            if (candidate.PoolKey is not null)
            {
                // Pool job: elect a free member; skip if none available or resolver is absent.
                if (pools is null || !pools.TryGetValue(candidate.PoolKey, out var ctx)) continue;
                foreach (var member in ctx.Members)
                    if (memberSlots!.GetValueOrDefault(member) < ctx.CapacityPerMember) { elected = member; break; }
                if (elected is null) continue;
                candidate.GateKey = elected;
            }

            Mark(candidate, owner, now, leaseUntil);
            if (await cas.ConditionalReplaceAsync(candidate, r => r.Status == JobStatus.Queued && r.Owner == null, ct))
                return candidate;

            // CAS loss on a pool job means our slot-count snapshot is stale; bail so the next drain iteration
            // re-elects with fresh counts rather than racing on stale data.
            if (elected is not null) return null;
            // Non-pool CAS loss: another worker claimed this row; fall through to the next runnable candidate.
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

    // --- claim internals ---

    private static (JobRecord? target, string? electedMember) ElectTarget(
        IReadOnlyList<JobRecord> candidates,
        IReadOnlyDictionary<string, PoolDispatchContext>? pools,
        Dictionary<string, int>? memberSlots)
    {
        foreach (var c in candidates)
        {
            if (c.PoolKey is not null)
            {
                if (pools is null || !pools.TryGetValue(c.PoolKey, out var ctx)) continue;
                string? free = null;
                foreach (var m in ctx.Members)
                    if (memberSlots!.GetValueOrDefault(m) < ctx.CapacityPerMember) { free = m; break; }
                if (free is null) continue;
                return (c, free);
            }
            return (c, null);
        }
        return (null, null);
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

    private async Task<IReadOnlyList<JobRecord>> SelectCandidates(
        DateTimeOffset now, IReadOnlyCollection<string> saturatedLanes,
        HashSet<string>? claimablePools, HashSet<(string WorkType, string WorkId)> busy, CancellationToken ct)
    {
        var gatedKeys = (await ActiveGates(now, ct)).Select(g => g.GateKey).ToHashSet(StringComparer.Ordinal);

        // The claim's heavy read (§19.3): the predicate + (VisibleAt, FirstSubmittedAt) order + LIMIT are pushed to the
        // store (ix_jobs_claim) — O(batch), not O(backlog). The lane/pool/gate/exclusive filter then runs over the
        // ordered window, returning runnable candidates in FIFO order; the claim tries them in turn (a CAS-loss on the
        // head falls through to the next), so a contended head doesn't waste the whole poll.
        //
        // The scan PAGES FORWARD past a fully-unclaimable window. A window's worth of unclaimable rows at the FIFO head
        // — an exhausted pool's backlog, a saturated lane, gated keys, or a busy exclusive work-item — must not consume
        // the scan and strand runnable work queued behind it; before this it stalled the whole pipeline indefinitely
        // with a healthy worker and no errors (JOBS-0007 head-of-line bug). We advance the page until a full batch of
        // claimable candidates is gathered OR the store returns a short page (end of the ready set), converging with the
        // in-memory ledger's full ordered scan. Each page stays O(batch) and index-served; deep paging only happens
        // while a large backlog is genuinely unclaimable, the same condition the §19.4 active-row guardrail surfaces.
        var batchSize = Math.Max(1, _options.ClaimScanBatch);
        var sort = SortBuilder<JobRecord>.Build(s => s.OrderBy(r => r.VisibleAt).ThenBy(r => r.FirstSubmittedAt));
        var candidates = new List<JobRecord>(batchSize);
        for (var page = 1; ; page++)
        {
            var scan = QueryDefinition.All.WithSort(sort).WithPagination(page, batchSize);
            var window = await JobRecord.Query(
                r => r.Status == JobStatus.Queued && r.VisibleAt <= now && r.CancelRequestedAt == null, scan, ct);
            if (window.Count == 0) break;

            foreach (var r in window)
            {
                if (!IsClaimable(r, saturatedLanes, claimablePools, gatedKeys, busy)) continue;
                candidates.Add(r);
                if (candidates.Count >= batchSize) return candidates;
            }

            if (window.Count < batchSize) break;   // short page → the ready set is exhausted
        }
        return candidates;
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

    private async Task<JobRecord?> ClaimViaTicket(JobRecord candidate, string owner, DateTimeOffset now, DateTimeOffset leaseUntil, string? electedMember, CancellationToken ct)
    {
        var ticket = new JobClaimTicket { JobId = candidate.Id, Owner = owner, CreatedAt = now };
        var ticketId = ticket.Id; // GUIDv7 — the bakery number
        await JobClaimTicket.Upsert(ticket, ct);

        await Task.Delay(_options.ClaimWindow, _clock, ct); // reserve the window so contenders become visible

        var tickets = await JobClaimTicket.Query(t => t.JobId == candidate.Id, ct);
        var winner = tickets.OrderBy(t => t.Id, StringComparer.Ordinal).FirstOrDefault();
        if (winner is null || winner.Id != ticketId)
        {
            await SafeRemoveTicket(ticketId, ct);
            return null; // lost the election — back off
        }

        // Re-read: another claimer/cancel may have changed it during the window.
        var fresh = await JobRecord.Get(candidate.Id, ct);
        if (fresh is null || fresh.Status != JobStatus.Queued || fresh.CancelRequestedAt is not null)
        {
            await RemoveTicketsFor(candidate.Id, ct);
            return null;
        }

        // Stamp the elected pool member onto the fresh record (pre-election stamp on `candidate` is not persisted).
        if (electedMember is not null)
            fresh.GateKey = electedMember;

        Mark(fresh, owner, now, leaseUntil);
        await JobRecord.Upsert(fresh, ct);
        await RemoveTicketsFor(candidate.Id, ct);
        return fresh;
    }

    private static void Mark(JobRecord r, string owner, DateTimeOffset now, DateTimeOffset leaseUntil)
    {
        r.Attempt++;
        r.Transitions.Add(new JobTransition { At = now, From = r.Status, To = JobStatus.Running, Note = $"claimed by {owner}" });
        r.Status = JobStatus.Running;
        r.Owner = owner;
        r.LeaseUntil = leaseUntil;
    }

    private static async Task RemoveTicketsFor(string jobId, CancellationToken ct)
    {
        foreach (var t in await JobClaimTicket.Query(x => x.JobId == jobId, ct))
            await JobClaimTicket.Remove(t.Id, ct);
    }

    private static async Task SafeRemoveTicket(string ticketId, CancellationToken ct)
    {
        try { await JobClaimTicket.Remove(ticketId, ct); } catch { /* GC is best-effort */ }
    }
}
