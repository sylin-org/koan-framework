namespace Koan.Jobs;

/// <summary>
/// In-memory <see cref="IJobLedger"/> for the Local tier and dockerless tests (JOBS-0005 §8). Stores and returns
/// <em>clones</em> so behavior converges with the durable tier (which round-trips through a store), and serializes
/// all mutations under one lock so <see cref="ClaimNext"/> is an atomic CAS. Non-durable: lost on restart.
/// </summary>
public sealed class InMemoryJobLedger : IJobLedger
{
    private readonly object _gate = new();
    private readonly Dictionary<string, JobRecord> _records = new(StringComparer.Ordinal);
    private readonly Dictionary<string, JobGate> _gates = new(StringComparer.Ordinal);

    public Task Append(JobRecord record, CancellationToken ct)
    {
        lock (_gate) _records[record.Id] = record.Clone();
        return Task.CompletedTask;
    }

    public Task AppendMany(IReadOnlyCollection<JobRecord> records, CancellationToken ct)
    {
        lock (_gate)
            foreach (var r in records) _records[r.Id] = r.Clone();
        return Task.CompletedTask;
    }

    public Task<JobRecord?> Get(string jobId, CancellationToken ct)
    {
        lock (_gate) return Task.FromResult(_records.TryGetValue(jobId, out var r) ? r.Clone() : null);
    }

    public Task<JobRecord?> FindActiveByCoalesceKey(string workType, string coalesceKey, CancellationToken ct)
    {
        lock (_gate)
        {
            var hit = _records.Values.FirstOrDefault(r =>
                !r.IsTerminal && r.WorkType == workType && r.CoalesceKey == coalesceKey);
            return Task.FromResult(hit?.Clone());
        }
    }

    public Task<JobRecord?> ClaimNext(string owner, DateTimeOffset now, DateTimeOffset leaseUntil,
        IReadOnlyCollection<string> saturatedLanes, CancellationToken ct)
    {
        lock (_gate)
        {
            // Per-entity serialization (§17.2): an exclusive job can't be claimed while another job for the same
            // (WorkType, WorkId) is already running.
            var busy = _records.Values
                .Where(r => r.Status == JobStatus.Running)
                .Select(r => (r.WorkType, r.WorkId))
                .ToHashSet();
            var candidate = _records.Values
                .Where(r => r.Status == JobStatus.Queued
                            && r.VisibleAt <= now
                            && r.CancelRequestedAt is null
                            && !saturatedLanes.Contains(r.Lane)
                            && !IsGated(r.GateKey, now)
                            && !(r.Exclusive && busy.Contains((r.WorkType, r.WorkId))))
                .OrderBy(r => r.VisibleAt)
                .ThenBy(r => r.FirstSubmittedAt)
                .FirstOrDefault();
            if (candidate is null) return Task.FromResult<JobRecord?>(null);

            candidate.Attempt++;
            Transition(candidate, JobStatus.Running, now, $"claimed by {owner}");
            candidate.Owner = owner;
            candidate.LeaseUntil = leaseUntil;
            return Task.FromResult<JobRecord?>(candidate.Clone());
        }
    }

    public Task Update(JobRecord record, CancellationToken ct)
    {
        lock (_gate) _records[record.Id] = record.Clone();
        return Task.CompletedTask;
    }

    public Task Progress(string jobId, double fraction, string? message, CancellationToken ct)
    {
        lock (_gate)
        {
            if (_records.TryGetValue(jobId, out var r))
            {
                r.ProgressFraction = fraction;
                r.ProgressMessage = message;
            }
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<JobRecord>> Stuck(DateTimeOffset now, CancellationToken ct)
    {
        lock (_gate)
        {
            var list = _records.Values
                .Where(r => r.Status == JobStatus.Running && r.LeaseUntil is { } l && l < now)
                .Select(r => r.Clone()).ToList();
            return Task.FromResult<IReadOnlyList<JobRecord>>(list);
        }
    }

    public Task<IReadOnlyList<JobRecord>> NonTerminal(CancellationToken ct)
    {
        lock (_gate)
        {
            var list = _records.Values.Where(r => !r.IsTerminal).Select(r => r.Clone()).ToList();
            return Task.FromResult<IReadOnlyList<JobRecord>>(list);
        }
    }

    public Task<IReadOnlyList<JobRecord>> InStage(string workType, string action, CancellationToken ct)
    {
        lock (_gate)
        {
            var list = _records.Values
                .Where(r => r.WorkType == workType && r.Action == action && r.Status == JobStatus.Queued)
                .Select(r => r.Clone()).ToList();
            return Task.FromResult<IReadOnlyList<JobRecord>>(list);
        }
    }

    public Task<IReadOnlyList<JobRecord>> Query(JobQuery query, CancellationToken ct)
    {
        lock (_gate)
        {
            var list = _records.Values
                .Where(r => (query.WorkType is null || r.WorkType == query.WorkType)
                            && (query.WorkId is null || r.WorkId == query.WorkId)
                            && (query.Action is null || r.Action == query.Action)
                            && (query.Status is null || r.Status == query.Status))
                .Select(r => r.Clone()).ToList();
            return Task.FromResult<IReadOnlyList<JobRecord>>(list);
        }
    }

    public Task SetGate(string gateKey, DateTimeOffset releaseAt, string? reason, CancellationToken ct)
    {
        lock (_gate)
        {
            if (_gates.TryGetValue(gateKey, out var existing) && existing.ReleaseAt >= releaseAt) return Task.CompletedTask;
            _gates[gateKey] = new JobGate { GateKey = gateKey, ReleaseAt = releaseAt, Reason = reason };
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<JobGate>> ActiveGates(DateTimeOffset now, CancellationToken ct)
    {
        lock (_gate)
        {
            var list = _gates.Values.Where(g => g.ReleaseAt > now)
                .Select(g => new JobGate { GateKey = g.GateKey, ReleaseAt = g.ReleaseAt, Reason = g.Reason }).ToList();
            return Task.FromResult<IReadOnlyList<JobGate>>(list);
        }
    }

    public Task<int> PurgeArchivable(DateTimeOffset olderThan, CancellationToken ct)
    {
        lock (_gate)
        {
            var stale = _records.Values
                .Where(r => r.Status is JobStatus.Completed or JobStatus.Cancelled
                            && r.LastSettledAt is { } s && s < olderThan)
                .Select(r => r.Id).ToList();
            foreach (var id in stale) _records.Remove(id);
            return Task.FromResult(stale.Count);
        }
    }

    public Task<int> PurgeFailed(DateTimeOffset olderThan, CancellationToken ct)
    {
        lock (_gate)
        {
            var stale = _records.Values
                .Where(r => r.Status is JobStatus.Failed or JobStatus.Dead
                            && r.LastSettledAt is { } s && s < olderThan)
                .Select(r => r.Id).ToList();
            foreach (var id in stale) _records.Remove(id);
            return Task.FromResult(stale.Count);
        }
    }

    public Task<int> TrimTerminal(string workType, int keep, CancellationToken ct)
    {
        if (keep <= 0) return Task.FromResult(0);
        lock (_gate)
        {
            var terminal = _records.Values
                .Where(r => r.WorkType == workType && r.IsTerminal)
                .OrderByDescending(r => r.LastSettledAt)
                .ToList();
            if (terminal.Count <= keep) return Task.FromResult(0);
            var excess = terminal.Skip(keep).Select(r => r.Id).ToList();
            foreach (var id in excess) _records.Remove(id);
            return Task.FromResult(excess.Count);
        }
    }

    private bool IsGated(string? gateKey, DateTimeOffset now)
        => gateKey is not null && _gates.TryGetValue(gateKey, out var g) && g.ReleaseAt > now;

    private static void Transition(JobRecord r, JobStatus to, DateTimeOffset at, string? note)
    {
        r.Transitions.Add(new JobTransition { At = at, From = r.Status, To = to, Note = note });
        r.Status = to;
    }
}
