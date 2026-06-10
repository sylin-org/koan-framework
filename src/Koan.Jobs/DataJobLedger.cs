using Koan.Data.Abstractions;
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
        var hits = await JobRecord.Query(r => r.WorkType == workType && r.CoalesceKey == coalesceKey, ct);
        return hits.FirstOrDefault(r => !r.IsTerminal);
    }

    public async Task<JobRecord?> ClaimNext(string owner, DateTimeOffset now, DateTimeOffset leaseUntil,
        IReadOnlyCollection<string> saturatedLanes, CancellationToken ct)
    {
        var candidate = await SelectCandidate(now, saturatedLanes, ct);
        if (candidate is null) return null;
        return _options.ClaimStrategy switch
        {
            ClaimStrategy.Ticket => await ClaimViaTicket(candidate, owner, now, leaseUntil, ct),
            _ => await ClaimOptimistic(candidate, owner, now, leaseUntil, ct),
        };
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

    // --- claim internals ---

    private async Task<JobRecord?> SelectCandidate(DateTimeOffset now, IReadOnlyCollection<string> saturatedLanes, CancellationToken ct)
    {
        var gatedKeys = (await ActiveGates(now, ct)).Select(g => g.GateKey).ToHashSet(StringComparer.Ordinal);
        // §17.2 exclusivity probe — the Running set is bounded by in-flight concurrency, not the backlog, so this is a
        // small, index-served query (Status prefix of ix_jobs_claim).
        var busy = (await JobRecord.Query(r => r.Status == JobStatus.Running, ct))
            .Select(r => (r.WorkType, r.WorkId))
            .ToHashSet();
        // The claim's heavy read (§19.3): predicate + (VisibleAt, FirstSubmittedAt) order + LIMIT ClaimScanBatch all
        // pushed to the store (ix_jobs_claim) — O(batch), not O(backlog). The lane/gate/exclusive filter then runs over
        // the small ordered window; if the whole window is blocked, the candidate waits for the next poll.
        var sort = SortBuilder<JobRecord>.Build(s => s.OrderBy(r => r.VisibleAt).ThenBy(r => r.FirstSubmittedAt));
        var scan = QueryDefinition.All.WithSort(sort).WithPagination(1, Math.Max(1, _options.ClaimScanBatch));
        var batch = await JobRecord.Query(
            r => r.Status == JobStatus.Queued && r.VisibleAt <= now && r.CancelRequestedAt == null, scan, ct);
        return batch.FirstOrDefault(r =>
            !saturatedLanes.Contains(r.Lane)
            && (r.GateKey is null || !gatedKeys.Contains(r.GateKey))
            && !(r.Exclusive && busy.Contains((r.WorkType, r.WorkId))));
    }

    private static async Task<JobRecord?> ClaimOptimistic(JobRecord candidate, string owner, DateTimeOffset now, DateTimeOffset leaseUntil, CancellationToken ct)
    {
        Mark(candidate, owner, now, leaseUntil);
        await JobRecord.Upsert(candidate, ct);
        return candidate;
    }

    private async Task<JobRecord?> ClaimViaTicket(JobRecord candidate, string owner, DateTimeOffset now, DateTimeOffset leaseUntil, CancellationToken ct)
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
