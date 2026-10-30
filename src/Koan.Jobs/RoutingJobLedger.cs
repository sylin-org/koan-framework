namespace Koan.Jobs;

/// <summary>
/// Per-type persistence routing (JOBS-0005 · <c>[JobPersistence]</c>). When a durable adapter is present, types
/// marked <see cref="JobPersistenceMode.InMemory"/> stay in the volatile ledger (fast, non-durable) while
/// <see cref="JobPersistenceMode.Auto"/>/<see cref="JobPersistenceMode.DataStore"/> types use the durable ledger.
/// </summary>
/// <remarks>
/// Routing rules: per-record operations route by <c>WorkType</c>; queue-wide reads union both ledgers;
/// <see cref="ClaimNext"/> claims from both (their job sets are disjoint by construction); resource gates are
/// mirrored to both ledgers so a cooperative backoff set by a durable job is honored by an in-memory job and
/// vice versa. This keeps the orchestrator entirely storage-agnostic — it still sees one <see cref="IJobLedger"/>.
/// </remarks>
internal sealed class RoutingJobLedger : IJobLedger
{
    private readonly IJobLedger _inMemory;
    private readonly IJobLedger _durable;
    private readonly JobTypeRegistry _registry;

    public RoutingJobLedger(IJobLedger inMemory, IJobLedger durable, JobTypeRegistry registry)
    {
        _inMemory = inMemory;
        _durable = durable;
        _registry = registry;
    }

    private IJobLedger For(string workType)
        => (_registry.Get(workType)?.Persistence ?? JobPersistenceMode.Auto) == JobPersistenceMode.InMemory
            ? _inMemory
            : _durable;

    public Task Append(JobRecord record, CancellationToken ct) => For(record.WorkType).Append(record, ct);

    public async Task AppendMany(IReadOnlyCollection<JobRecord> records, CancellationToken ct)
    {
        foreach (var group in records.GroupBy(r => For(r.WorkType)))
            await group.Key.AppendMany(group.ToList(), ct);
    }

    // jobId carries no type; a job lives in exactly one ledger, so probe durable first then volatile.
    public async Task<JobRecord?> Get(string jobId, CancellationToken ct)
        => await _durable.Get(jobId, ct) ?? await _inMemory.Get(jobId, ct);

    public Task<JobRecord?> FindCoalescing(string workType, string coalesceKey, CancellationToken ct)
        => For(workType).FindCoalescing(workType, coalesceKey, ct);

    public Task<JobRecord?> AppendCoalescing(JobRecord record, CancellationToken ct)
        => For(record.WorkType).AppendCoalescing(record, ct);

    public Task<JobRecord?> TryRequeue(JobRecord record, string claimToken, CancellationToken ct)
        => For(record.WorkType).TryRequeue(record, claimToken, ct);

    // Disjoint job sets: claiming durable-then-volatile never double-claims; gates are mirrored so each honors all.
    public async Task<JobRecord?> ClaimNext(string owner, DateTimeOffset now, DateTimeOffset leaseUntil,
        IReadOnlyCollection<string> saturatedLanes, CancellationToken ct,
        IReadOnlyDictionary<string, PoolDispatchContext>? pools = null)
        => await _durable.ClaimNext(owner, now, leaseUntil, saturatedLanes, ct, pools)
           ?? await _inMemory.ClaimNext(owner, now, leaseUntil, saturatedLanes, ct, pools);

    public Task<bool> Update(JobRecord record, CancellationToken ct) => For(record.WorkType).Update(record, ct);

    public async Task Progress(string jobId, string claimToken, double fraction, string? message, CancellationToken ct)
    {
        if (await _durable.Get(jobId, ct) is not null) await _durable.Progress(jobId, claimToken, fraction, message, ct);
        else await _inMemory.Progress(jobId, claimToken, fraction, message, ct);
    }

    // A running job lives in exactly one ledger; probe durable then volatile like Get.
    public async Task<JobRecord?> RenewLease(string jobId, string claimToken, DateTimeOffset leaseUntil, CancellationToken ct)
        => await _durable.RenewLease(jobId, claimToken, leaseUntil, ct)
           ?? await _inMemory.RenewLease(jobId, claimToken, leaseUntil, ct);

    public Task<JobRecord?> TrySettle(JobRecord record, string claimToken, CancellationToken ct)
        => For(record.WorkType).TrySettle(record, claimToken, ct);

    // A successor lives in its predecessor's ledger; probe durable then volatile like Get.
    public async Task<bool> ReleaseHeld(string successorId, string holderToken, bool cancel, DateTimeOffset now, CancellationToken ct)
        => await _durable.ReleaseHeld(successorId, holderToken, cancel, now, ct)
           || await _inMemory.ReleaseHeld(successorId, holderToken, cancel, now, ct);

    public async Task<bool> DeleteHeld(string successorId, string holderToken, CancellationToken ct)
        => await _durable.DeleteHeld(successorId, holderToken, ct)
           || await _inMemory.DeleteHeld(successorId, holderToken, ct);

    public async Task<IReadOnlyList<JobRecord>> Held(int page, int limit, CancellationToken ct)
        => Concat(await _durable.Held(page, limit, ct), await _inMemory.Held(page, limit, ct));

    // Reservations (PMC-056) live in exactly one tier's ledger, like the rows they point at.
    public async Task<bool> TryReserve(string jobId, string hand, DateTimeOffset reservedUntil, DateTimeOffset now, CancellationToken ct)
        => await _durable.TryReserve(jobId, hand, reservedUntil, now, ct)
           || await _inMemory.TryReserve(jobId, hand, reservedUntil, now, ct);

    public async Task<IReadOnlyList<JobRecord>> ReservationCandidates(DateTimeOffset now, int limit, CancellationToken ct)
        => Concat(await _durable.ReservationCandidates(now, limit, ct), await _inMemory.ReservationCandidates(now, limit, ct));

    public async Task<IReadOnlyList<JobRecord>> Reservations(CancellationToken ct)
        => Concat(await _durable.Reservations(ct), await _inMemory.Reservations(ct));

    // Bounded sweeps take the durable page first and fill what is left of the limit from the volatile tier.
    public async Task<IReadOnlyList<JobRecord>> Stuck(DateTimeOffset now, int limit, CancellationToken ct)
    {
        var durable = await _durable.Stuck(now, limit, ct);
        return Concat(durable, await _inMemory.Stuck(now, limit - durable.Count, ct));
    }

    public async Task<IReadOnlyList<JobRecord>> Orphaned(IReadOnlyCollection<string> liveOwners, int limit, CancellationToken ct)
    {
        var durable = await _durable.Orphaned(liveOwners, limit, ct);
        return Concat(durable, await _inMemory.Orphaned(liveOwners, limit - durable.Count, ct));
    }

    public async Task<IReadOnlyDictionary<string, int>> RunningLoad(IReadOnlyCollection<string> owners, CancellationToken ct)
    {
        var load = new Dictionary<string, int>(await _durable.RunningLoad(owners, ct), StringComparer.Ordinal);
        foreach (var (owner, count) in await _inMemory.RunningLoad(owners, ct))
            load[owner] = load.GetValueOrDefault(owner) + count;
        return load;
    }

    // A work type's rows live in one ledger. A query across types reads the same newest-first window from both tiers
    // and pages the merge — bounded by the page the caller asked for.
    public async Task<IReadOnlyList<JobRecord>> Query(JobQuery query, CancellationToken ct)
    {
        if (query.WorkType is { } workType) return await For(workType).Query(query, ct);
        var window = query with { Page = 1, Size = query.Page * query.Size };
        return Concat(await _durable.Query(window, ct), await _inMemory.Query(window, ct))
            .OrderByDescending(r => r.FirstSubmittedAt)
            .ThenByDescending(r => r.Id, StringComparer.Ordinal)
            .Skip((query.Page - 1) * query.Size)
            .Take(query.Size)
            .ToList();
    }

    // Mirror gates to both ledgers: a backoff must hold across persistence tiers sharing a resource.
    public async Task SetGate(string gateKey, DateTimeOffset releaseAt, string? reason, CancellationToken ct)
    {
        await _durable.SetGate(gateKey, releaseAt, reason, ct);
        await _inMemory.SetGate(gateKey, releaseAt, reason, ct);
    }

    public async Task<IReadOnlyList<JobGateRecord>> ActiveGates(DateTimeOffset now, CancellationToken ct)
    {
        var gates = new Dictionary<string, JobGateRecord>(StringComparer.Ordinal);
        foreach (var g in await _durable.ActiveGates(now, ct)) gates[g.GateKey] = g;
        foreach (var g in await _inMemory.ActiveGates(now, ct)) gates.TryAdd(g.GateKey, g);
        return gates.Values.ToList();
    }

    // Gates are mirrored, so each ledger sweeps its own copy; the page bound holds across both.
    public async Task<int> PurgeReleasedGates(DateTimeOffset now, int limit, CancellationToken ct)
    {
        var removed = await _durable.PurgeReleasedGates(now, limit, ct);
        return removed + await _inMemory.PurgeReleasedGates(now, limit - removed, ct);
    }

    public async Task<int> PurgeArchivable(DateTimeOffset olderThan, int limit, CancellationToken ct)
    {
        var removed = await _durable.PurgeArchivable(olderThan, limit, ct);
        return removed + await _inMemory.PurgeArchivable(olderThan, limit - removed, ct);
    }

    public async Task<int> PurgeFailed(DateTimeOffset olderThan, int limit, CancellationToken ct)
    {
        var removed = await _durable.PurgeFailed(olderThan, limit, ct);
        return removed + await _inMemory.PurgeFailed(olderThan, limit - removed, ct);
    }

    // A work-type's rows live in exactly one ledger (persistence routing), so trim the owning one.
    public Task<int> TrimTerminal(string workType, int keep, int limit, CancellationToken ct)
        => For(workType).TrimTerminal(workType, keep, limit, ct);

    public Task<long> CountActive(string workType, CancellationToken ct)
        => For(workType).CountActive(workType, ct);

    // Merge the two tiers' snapshots: sum the counts, take the max oldest-age.
    public async Task<JobsHealthSnapshot> HealthSnapshot(DateTimeOffset now, CancellationToken ct)
    {
        var d = await _durable.HealthSnapshot(now, ct);
        var m = await _inMemory.HealthSnapshot(now, ct);
        return new JobsHealthSnapshot(
            d.Queued + m.Queued,
            d.Running + m.Running,
            d.ReclaimBacklog + m.ReclaimBacklog,
            d.OldestQueuedAge >= m.OldestQueuedAge ? d.OldestQueuedAge : m.OldestQueuedAge);
    }

    private static IReadOnlyList<JobRecord> Concat(IReadOnlyList<JobRecord> a, IReadOnlyList<JobRecord> b)
    {
        if (a.Count == 0) return b;
        if (b.Count == 0) return a;
        var list = new List<JobRecord>(a.Count + b.Count);
        list.AddRange(a);
        list.AddRange(b);
        return list;
    }
}
