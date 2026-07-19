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

    public Task<JobRecord?> FindActiveByCoalesceKey(string workType, string coalesceKey, CancellationToken ct)
        => For(workType).FindActiveByCoalesceKey(workType, coalesceKey, ct);

    // Disjoint job sets: claiming durable-then-volatile never double-claims; gates are mirrored so each honors all.
    public async Task<JobRecord?> ClaimNext(string owner, DateTimeOffset now, DateTimeOffset leaseUntil,
        IReadOnlyCollection<string> saturatedLanes, CancellationToken ct,
        IReadOnlyDictionary<string, PoolDispatchContext>? pools = null)
        => await _durable.ClaimNext(owner, now, leaseUntil, saturatedLanes, ct, pools)
           ?? await _inMemory.ClaimNext(owner, now, leaseUntil, saturatedLanes, ct, pools);

    public Task Update(JobRecord record, CancellationToken ct) => For(record.WorkType).Update(record, ct);

    public async Task Progress(string jobId, double fraction, string? message, CancellationToken ct)
    {
        if (await _durable.Get(jobId, ct) is not null) await _durable.Progress(jobId, fraction, message, ct);
        else await _inMemory.Progress(jobId, fraction, message, ct);
    }

    public async Task<IReadOnlyList<JobRecord>> Stuck(DateTimeOffset now, CancellationToken ct)
        => Concat(await _durable.Stuck(now, ct), await _inMemory.Stuck(now, ct));

    public async Task<IReadOnlyList<JobRecord>> NonTerminal(CancellationToken ct)
        => Concat(await _durable.NonTerminal(ct), await _inMemory.NonTerminal(ct));

    public Task<IReadOnlyList<JobRecord>> InStage(string workType, string action, CancellationToken ct)
        => For(workType).InStage(workType, action, ct);

    public async Task<IReadOnlyList<JobRecord>> Query(JobQuery query, CancellationToken ct)
        => Concat(await _durable.Query(query, ct), await _inMemory.Query(query, ct));

    // Mirror gates to both ledgers: a backoff must hold across persistence tiers sharing a resource.
    public async Task SetGate(string gateKey, DateTimeOffset releaseAt, string? reason, CancellationToken ct)
    {
        await _durable.SetGate(gateKey, releaseAt, reason, ct);
        await _inMemory.SetGate(gateKey, releaseAt, reason, ct);
    }

    public async Task<IReadOnlyList<JobGate>> ActiveGates(DateTimeOffset now, CancellationToken ct)
    {
        var gates = new Dictionary<string, JobGate>(StringComparer.Ordinal);
        foreach (var g in await _durable.ActiveGates(now, ct)) gates[g.GateKey] = g;
        foreach (var g in await _inMemory.ActiveGates(now, ct)) gates.TryAdd(g.GateKey, g);
        return gates.Values.ToList();
    }

    public async Task<int> PurgeArchivable(DateTimeOffset olderThan, CancellationToken ct)
        => await _durable.PurgeArchivable(olderThan, ct) + await _inMemory.PurgeArchivable(olderThan, ct);

    public async Task<int> PurgeFailed(DateTimeOffset olderThan, CancellationToken ct)
        => await _durable.PurgeFailed(olderThan, ct) + await _inMemory.PurgeFailed(olderThan, ct);

    // A work-type's rows live in exactly one ledger (persistence routing), so trim the owning one.
    public Task<int> TrimTerminal(string workType, int keep, CancellationToken ct)
        => For(workType).TrimTerminal(workType, keep, ct);

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
