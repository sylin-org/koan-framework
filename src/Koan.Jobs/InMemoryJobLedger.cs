using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.Extensions.Options;

namespace Koan.Jobs;

/// <summary>
/// In-memory <see cref="IJobLedger"/> for the Local tier and dockerless tests (JOBS-0005 §8). Stores and returns
/// <em>clones</em> so behavior converges with the durable tier (which round-trips through a store), and serializes
/// all mutations under one lock — the claim lock — so <see cref="ClaimNext"/> is an atomic CAS. Non-durable: lost on
/// restart.
/// <para>The volatile tier keeps its rows in structures shaped by the reads it serves (JOBS-0010): queued rows per lane
/// in claim order and, across lanes, the open ones in visible order; running rows with the work items and pool members
/// they hold; the coalescing groups of queued jobs; each work item's jobs; the active jobs of each work type, counted in
/// one step per write; and settled rows in a history of at most <see cref="JobsOptions.InMemoryTerminalCapacity"/>, the
/// oldest evicted first. A claim walks each lane's due rows from its head and stops at the first claimable one.</para>
/// <para>A stored row is never changed in place: every write stores a new copy. So a read by job id, a status read by work
/// item, a count and a query read without the claim lock and never wait for a claim; a query that names no work item
/// scans the rows outside the lock, keeping only the rows up to its page.</para>
/// </summary>
internal sealed class InMemoryJobLedger : IJobLedger
{
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<string, JobRecord> _records = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<(string WorkType, string WorkId), ImmutableList<string>> _byItem = new();
    private readonly ConcurrentDictionary<string, long> _active = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SortedSet<JobRecord>> _queued = new(StringComparer.Ordinal);
    private readonly SortedSet<JobRecord> _open = new(ClaimOrder.Instance);
    private readonly HashSet<string> _reserved = new(StringComparer.Ordinal);
    private readonly HashSet<string> _running = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Type, string Id, JobDataRoute Route), int> _busy = new();
    private readonly Dictionary<string, int> _memberLoad = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, HashSet<string>> _groups = new();
    private readonly LinkedList<string> _settled = new();
    private readonly Dictionary<string, LinkedListNode<string>> _settledNodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, JobGateRecord> _gates = new(StringComparer.Ordinal);
    // JOBS-0008: per-lane WFQ virtual time. The Local tier is single-process, so this in-process dict IS the whole
    // fairness state — the same LaneFairSelector the durable tier runs over its own per-node state.
    private readonly Dictionary<string, double> _virtual = new(StringComparer.Ordinal);
    private readonly Func<string, double> _weight;
    private readonly int _settledCapacity;
    private readonly JobCompletions? _completions;

    public InMemoryJobLedger(IOptions<JobsOptions>? options = null, JobCompletions? completions = null)
    {
        var weights = options?.Value.LaneWeights;
        _weight = weights is null ? _ => 1.0 : lane => weights.GetValueOrDefault(lane, 1.0);
        _settledCapacity = Math.Max(0, options?.Value.InMemoryTerminalCapacity ?? new JobsOptions().InMemoryTerminalCapacity);
        _completions = completions;
    }

    /// <summary>The lock every write and claim takes. The specs that prove reads never wait for a claim hold it.</summary>
    internal object ClaimLock => _gate;

    public Task Append(JobRecord record, CancellationToken ct)
    {
        lock (_gate) Store(record);
        return Task.CompletedTask;
    }

    public Task AppendMany(IReadOnlyCollection<JobRecord> records, CancellationToken ct)
    {
        lock (_gate)
            foreach (var r in records) Store(r);
        return Task.CompletedTask;
    }

    /// <summary>One keyed read, without the claim lock.</summary>
    public Task<JobRecord?> Get(string jobId, CancellationToken ct)
        => Task.FromResult(_records.TryGetValue(jobId, out var r) ? r.Clone() : null);

    public Task<JobRecord?> FindCoalescing(string workType, string coalesceKey, CancellationToken ct)
    {
        lock (_gate) return Task.FromResult(QueuedOf(JobRecord.SlotFor(coalesceKey)).FirstOrDefault()?.Clone());
    }

    public Task<JobRecord?> AppendCoalescing(JobRecord record, CancellationToken ct)
    {
        lock (_gate)
        {
            var stored = Own(record);
            if (record.CoalesceKey is { } key)
            {
                if (QueuedOf(JobRecord.SlotFor(key)).FirstOrDefault() is { } queued) return Task.FromResult<JobRecord?>(queued.Clone());
            }
            Put(stored);
            record.Revision = stored.Revision;
            return Task.FromResult<JobRecord?>(null);
        }
    }

    public Task<JobRecord?> ClaimNext(string owner, DateTimeOffset now, DateTimeOffset leaseUntil,
        IReadOnlyCollection<string> saturatedLanes, CancellationToken ct,
        IReadOnlyDictionary<string, PoolDispatchContext>? pools = null)
    {
        lock (_gate)
        {
            // JOBS-0008: gather each lane's oldest claimable head, then pick the fairest lane (weighted fair queuing over
            // per-lane virtual time). Within a lane the head is the oldest due row that is open to this hand, not held
            // by a running job on the same work item (§17.2), and — pool-XOR-gate — whose pool has a free member or
            // whose gate is open. Each lane is walked from its head in claim order and only while rows are due.
            var heads = new Dictionary<string, JobRecord>(StringComparer.Ordinal);
            foreach (var (lane, queued) in _queued)
            {
                if (saturatedLanes.Contains(lane)) continue;
                foreach (var r in queued)
                {
                    if (r.VisibleAt > now) break;
                    if (r.CancelRequestedAt is not null || !OpenTo(r, owner, now)) continue;
                    if (r.Exclusive && _busy.ContainsKey(JobDataRoute.Identity(r))) continue;
                    if (r.PoolKey is not null)
                    {
                        if (pools is null || !pools.TryGetValue(r.PoolKey, out var pool) || FreeMember(pool) is null) continue;
                    }
                    else if (IsGated(r.GateKey, now)) continue;
                    heads[lane] = r;
                    break;
                }
            }

            if (heads.Count == 0) return Task.FromResult<JobRecord?>(null);

            var chosen = LaneFairSelector.Pick(heads.Keys, _virtual)!;
            var claimed = heads[chosen].Clone();

            // Stamp the elected pool member atomically with the claim transition.
            if (claimed.PoolKey is not null) claimed.GateKey = FreeMember(pools![claimed.PoolKey]);

            claimed.Attempt++;
            claimed.ReleaseCoalesceSlot();   // a claimed job no longer takes submissions (JOBS-0006 trailing edge)
            Transition(claimed, JobStatus.Running, now, $"claimed by {owner}");
            claimed.Owner = owner;
            claimed.LeaseUntil = leaseUntil;
            // PMC-056: the claim consumes the reservation cookie (if this hand held one).
            claimed.ReservedFor = null;
            claimed.ReservedUntil = null;
            Put(claimed);

            _virtual[chosen] = LaneFairSelector.Charged(_virtual.GetValueOrDefault(chosen), _weight(chosen));
            return Task.FromResult<JobRecord?>(claimed.Clone());
        }
    }

    /// <summary>A compare on the revision the caller read: false, and nothing written, when the row changed since.</summary>
    public Task<bool> Update(JobRecord record, CancellationToken ct)
    {
        lock (_gate)
        {
            if (!_records.TryGetValue(record.Id, out var stored) || stored.Revision != record.Revision)
                return Task.FromResult(false);
            Store(record);
            return Task.FromResult(true);
        }
    }

    // --- reservation dispatch (PMC-056) ---

    public Task<bool> TryReserve(string jobId, string hand, DateTimeOffset reservedUntil, DateTimeOffset now, CancellationToken ct)
    {
        lock (_gate)
        {
            if (!_records.TryGetValue(jobId, out var r)
                || r.Status != JobStatus.Queued
                || r.ReservedFor is not null)
            {
                return Task.FromResult(false);
            }
            var next = r.Clone();
            next.ReservedFor = hand;          // not part of the claim order, so the row keeps its place
            next.ReservedUntil = reservedUntil;
            Put(next);
            return Task.FromResult(true);
        }
    }

    /// <summary>The oldest due open rows, walked from the head of the queued rows in visible order.</summary>
    public Task<IReadOnlyList<JobRecord>> ReservationCandidates(DateTimeOffset now, int limit, CancellationToken ct)
    {
        var list = new List<JobRecord>();
        if (limit <= 0) return Task.FromResult<IReadOnlyList<JobRecord>>(list);
        lock (_gate)
        {
            foreach (var r in _open)
            {
                if (r.VisibleAt > now || list.Count == limit) break;
                list.Add(r.Clone());
            }
        }
        return Task.FromResult<IReadOnlyList<JobRecord>>(list);
    }

    public Task<IReadOnlyList<JobRecord>> Reservations(CancellationToken ct)
    {
        lock (_gate)
            return Task.FromResult<IReadOnlyList<JobRecord>>(_reserved.Select(id => _records[id].Clone()).ToList());
    }

    public Task<IReadOnlyList<JobRecord>> Stuck(DateTimeOffset now, int limit, CancellationToken ct)
    {
        lock (_gate)
        {
            var list = Running()
                .Where(r => r.LeaseUntil is { } l && l < now)
                .Take(Math.Max(0, limit))
                .Select(r => r.Clone()).ToList();
            return Task.FromResult<IReadOnlyList<JobRecord>>(list);
        }
    }

    public Task<IReadOnlyList<JobRecord>> Orphaned(IReadOnlyCollection<string> liveOwners, int limit, CancellationToken ct)
    {
        lock (_gate)
        {
            var list = Running()
                .Where(r => r.Owner is { } owner && !liveOwners.Contains(owner))
                .Take(Math.Max(0, limit))
                .Select(r => r.Clone()).ToList();
            return Task.FromResult<IReadOnlyList<JobRecord>>(list);
        }
    }

    public Task<IReadOnlyDictionary<string, int>> RunningLoad(IReadOnlyCollection<string> owners, CancellationToken ct)
    {
        lock (_gate)
        {
            var load = Running()
                .Where(r => r.Owner is { } owner && owners.Contains(owner))
                .GroupBy(r => r.Owner!, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
            return Task.FromResult<IReadOnlyDictionary<string, int>>(load);
        }
    }

    /// <summary>A page of matches, newest first, read without the claim lock. A query naming a work item reads that
    /// item's jobs; any other scans the rows, keeping only the rows up to the page it reads.</summary>
    public Task<IReadOnlyList<JobRecord>> Query(JobQuery query, CancellationToken ct)
    {
        var matches = JobLedgerPredicates.Matcher(query);
        IEnumerable<JobRecord> candidates = query is { WorkType: { } type, WorkId: { } id }
            ? _byItem.TryGetValue((type, id), out var ids) ? Rows(ids) : []
            : _records.Select(pair => pair.Value);

        var keep = (int)Math.Min(int.MaxValue, (long)query.Page * query.Size);
        var newest = new PriorityQueue<JobRecord, JobRecord>(NewestFirst.Oldest);
        foreach (var row in candidates)
        {
            if (!matches(row)) continue;
            if (newest.Count < keep) newest.Enqueue(row, row);
            else if (NewestFirst.Instance.Compare(row, newest.Peek()) < 0) newest.EnqueueDequeue(row, row);
        }

        var page = newest.UnorderedItems.Select(item => item.Element)
            .Order(NewestFirst.Instance)
            .Skip((query.Page - 1) * query.Size)
            .Select(row => row.Clone())
            .ToList();
        return Task.FromResult<IReadOnlyList<JobRecord>>(page);
    }

    public Task SetGate(string gateKey, DateTimeOffset releaseAt, string? reason, CancellationToken ct)
    {
        lock (_gate)
        {
            if (_gates.TryGetValue(gateKey, out var existing) && existing.ReleaseAt >= releaseAt) return Task.CompletedTask;
            _gates[gateKey] = new JobGateRecord { Id = JobGateRecord.IdFor(gateKey), GateKey = gateKey, ReleaseAt = releaseAt, Reason = reason };
        }
        return Task.CompletedTask;
    }

    public Task<int> PurgeReleasedGates(DateTimeOffset now, int limit, CancellationToken ct)
    {
        if (limit <= 0) return Task.FromResult(0);
        lock (_gate)
        {
            var released = _gates.Values.Where(g => g.ReleaseAt <= now).Take(limit).Select(g => g.GateKey).ToList();
            foreach (var key in released) _gates.Remove(key);
            return Task.FromResult(released.Count);
        }
    }

    public Task<IReadOnlyList<JobGateRecord>> ActiveGates(DateTimeOffset now, CancellationToken ct)
    {
        lock (_gate)
        {
            var list = _gates.Values.Where(g => g.ReleaseAt > now)
                .Select(g => new JobGateRecord { Id = g.Id, GateKey = g.GateKey, ReleaseAt = g.ReleaseAt, Reason = g.Reason }).ToList();
            return Task.FromResult<IReadOnlyList<JobGateRecord>>(list);
        }
    }

    public Task<int> PurgeArchivable(DateTimeOffset olderThan, int limit, CancellationToken ct)
        => RemoveSettled(r => r.Status is JobStatus.Completed or JobStatus.Cancelled
                              && r.LastSettledAt is { } s && s < olderThan, limit);

    public Task<int> PurgeFailed(DateTimeOffset olderThan, int limit, CancellationToken ct)
        => RemoveSettled(r => r.Status is JobStatus.Failed or JobStatus.Dead
                              && r.LastSettledAt is { } s && s < olderThan, limit);

    public Task<int> TrimTerminal(string workType, int keep, int limit, CancellationToken ct)
    {
        if (keep <= 0 || limit <= 0) return Task.FromResult(0);
        lock (_gate)
        {
            var excess = Settled()
                .Where(r => r.WorkType == workType)
                .OrderByDescending(r => r.LastSettledAt)
                .Skip(keep)
                .Take(limit)
                .Select(r => r.Id).ToList();
            foreach (var id in excess) Remove(id);
            return Task.FromResult(excess.Count);
        }
    }

    /// <summary>The queued and running jobs of <paramref name="workType"/>, counted as rows change state: one keyed read
    /// without the claim lock.</summary>
    public Task<long> CountActive(string workType, CancellationToken ct)
        => Task.FromResult(_active.TryGetValue(workType, out var count) ? count : 0L);

    public Task<JobsHealthSnapshot> HealthSnapshot(DateTimeOffset now, CancellationToken ct)
    {
        lock (_gate)
        {
            long queued = _queued.Values.Sum(lane => (long)lane.Count);
            long running = _running.Count;
            long reclaim = Running().Count(r => r.LeaseUntil is { } l && l < now);
            DateTimeOffset? oldestDue = null;
            foreach (var lane in _queued.Values)
                if (lane.Min is { } head && head.VisibleAt <= now && (oldestDue is null || head.VisibleAt < oldestDue))
                    oldestDue = head.VisibleAt;
            var age = oldestDue is { } od && now > od ? now - od : TimeSpan.Zero;
            return Task.FromResult(new JobsHealthSnapshot(queued, running, reclaim, age));
        }
    }

    // --- structures (written under the lock; _records, _byItem and _active are also read without it) ---

    /// <summary>A clone of the caller's record to store. Its id is minted on the caller's record first, so both carry
    /// the same one.</summary>
    private static JobRecord Own(JobRecord record)
    {
        _ = record.Id;
        return record.Clone();
    }

    /// <summary>Store a clone of the caller's record; the caller's record takes the stored revision.</summary>
    private void Store(JobRecord record)
    {
        var row = Own(record);
        Put(row);
        record.Revision = row.Revision;
    }

    /// <summary>Store <paramref name="row"/> under a fresh revision, replacing any row with its id, and file it where its
    /// state belongs. The active count of its work type changes in one step. A row that settles is stored, then told to
    /// the waiting handles, then — beyond the settled history — evicted, so a handle reads it or hears it.</summary>
    private void Put(JobRecord row)
    {
        row.Revision = Guid.NewGuid();
        var known = _records.TryGetValue(row.Id, out var previous);
        if (known) Unfile(previous!);
        _records[row.Id] = row;
        if (!known)
            _byItem.AddOrUpdate((row.WorkType, row.WorkId), _ => [row.Id], (_, ids) => ids.Add(row.Id));
        File(row);
        var delta = (IsActive(row) ? 1 : 0) - (known && IsActive(previous!) ? 1 : 0);
        if (delta != 0) CountActive(row.WorkType, delta);
        if (row.IsTerminal && previous is not { IsTerminal: true }) _completions?.Settled(row);
        while (_settled.Count > _settledCapacity) Remove(_settled.First!.Value);   // evict the oldest settled
    }

    private void Remove(string id)
    {
        if (!_records.TryGetValue(id, out var row)) return;
        Unfile(row);
        if (IsActive(row)) CountActive(row.WorkType, -1);
        _records.TryRemove(id, out _);
        var item = (row.WorkType, row.WorkId);
        if (_byItem.TryGetValue(item, out var ids))
        {
            var rest = ids.Remove(id);
            if (rest.IsEmpty) _byItem.TryRemove(item, out _);
            else _byItem[item] = rest;
        }
    }

    private void File(JobRecord row)
    {
        switch (row.Status)
        {
            case JobStatus.Queued:
                if (!_queued.TryGetValue(row.Lane, out var lane)) _queued[row.Lane] = lane = new SortedSet<JobRecord>(ClaimOrder.Instance);
                lane.Add(row);
                if (IsOpen(row)) _open.Add(row);
                if (row.ReservedFor is not null) _reserved.Add(row.Id);
                if (row.CoalesceGroup is { } joined)
                {
                    if (!_groups.TryGetValue(joined, out var members)) _groups[joined] = members = new HashSet<string>(StringComparer.Ordinal);
                    members.Add(row.Id);
                }
                break;
            case JobStatus.Running:
                _running.Add(row.Id);
                var identity = JobDataRoute.Identity(row);
                _busy[identity] = _busy.GetValueOrDefault(identity) + 1;
                if (row.GateKey is { } member) _memberLoad[member] = _memberLoad.GetValueOrDefault(member) + 1;
                break;
            case JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled or JobStatus.Dead:
                _settledNodes[row.Id] = _settled.AddLast(row.Id);
                break;
        }
    }

    private void Unfile(JobRecord row)
    {
        switch (row.Status)
        {
            case JobStatus.Queued:
                if (_queued.TryGetValue(row.Lane, out var lane))
                {
                    lane.Remove(row);
                    if (lane.Count == 0) _queued.Remove(row.Lane);
                }
                _open.Remove(row);
                _reserved.Remove(row.Id);
                if (row.CoalesceGroup is { } left && _groups.TryGetValue(left, out var leftMembers))
                {
                    leftMembers.Remove(row.Id);
                    if (leftMembers.Count == 0) _groups.Remove(left);
                }
                break;
            case JobStatus.Running:
                _running.Remove(row.Id);
                Decrement(_busy, JobDataRoute.Identity(row));
                if (row.GateKey is { } member) Decrement(_memberLoad, member);
                break;
            case JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled or JobStatus.Dead:
                if (_settledNodes.Remove(row.Id, out var node)) _settled.Remove(node);
                break;
        }
    }

    private void CountActive(string workType, long delta) => _active.AddOrUpdate(workType, delta, (_, count) => count + delta);

    private static bool IsActive(JobRecord row) => row.Status is JobStatus.Queued or JobStatus.Running;

    /// <summary>A queued row a reservation pass may hand out: unreserved and not asked to cancel.</summary>
    private static bool IsOpen(JobRecord row) => row.ReservedFor is null && row.CancelRequestedAt is null;

    private static void Decrement<TKey>(Dictionary<TKey, int> counts, TKey key) where TKey : notnull
    {
        if (!counts.TryGetValue(key, out var count)) return;
        if (count <= 1) counts.Remove(key);
        else counts[key] = count - 1;
    }

    private IEnumerable<JobRecord> Rows(IEnumerable<string> ids)
    {
        foreach (var id in ids)
            if (_records.TryGetValue(id, out var row)) yield return row;
    }

    private IEnumerable<JobRecord> Running() => _running.Select(id => _records[id]);

    private IEnumerable<JobRecord> Settled() => _settled.Select(id => _records[id]);

    private Task<int> RemoveSettled(Func<JobRecord, bool> predicate, int limit)
    {
        if (limit <= 0) return Task.FromResult(0);
        lock (_gate)
        {
            var stale = Settled().Where(predicate).Take(limit).Select(r => r.Id).ToList();
            foreach (var id in stale) Remove(id);
            return Task.FromResult(stale.Count);
        }
    }

    /// <summary>The queued, uncancelled jobs of a coalescing group.</summary>
    private IEnumerable<JobRecord> QueuedOf(Guid group)
        => _groups.TryGetValue(group, out var members)
            ? members.Select(id => _records[id]).Where(row => row.Status == JobStatus.Queued && row.CancelRequestedAt is null)
            : [];

    /// <summary>The first pool member with a free slot, or null.</summary>
    private string? FreeMember(PoolDispatchContext pool)
        => pool.Members.FirstOrDefault(m => _memberLoad.GetValueOrDefault(m) < pool.CapacityPerMember);

    private bool IsGated(string? gateKey, DateTimeOffset now)
        => gateKey is not null && _gates.TryGetValue(gateKey, out var g) && g.ReleaseAt > now;

    /// <summary>PMC-056 claim eligibility: a fresh reservation binds its named hand; unreserved or lapsed rows
    /// are open to anyone. Mirrored by both durable claim predicates so the tiers converge (ARCH-0079).</summary>
    private static bool OpenTo(JobRecord r, string owner, DateTimeOffset now)
        => r.ReservedFor is null
           || string.Equals(r.ReservedFor, owner, StringComparison.Ordinal)
           || (r.ReservedUntil is { } until && until < now);

    private static void Transition(JobRecord r, JobStatus to, DateTimeOffset at, string? note)
    {
        r.Transitions.Add(new JobTransition { At = at, From = r.Status, To = to, Note = note });
        r.Status = to;
    }

    /// <summary>The claim order: visible first, then submitted first, then by id so no two rows tie.</summary>
    private sealed class ClaimOrder : IComparer<JobRecord>
    {
        public static readonly ClaimOrder Instance = new();

        public int Compare(JobRecord? x, JobRecord? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;
            var byVisible = x.VisibleAt.CompareTo(y.VisibleAt);
            if (byVisible != 0) return byVisible;
            var bySubmitted = x.FirstSubmittedAt.CompareTo(y.FirstSubmittedAt);
            return bySubmitted != 0 ? bySubmitted : string.CompareOrdinal(x.Id, y.Id);
        }
    }

    /// <summary>The order a query reads: submitted last first, then by id descending.</summary>
    private sealed class NewestFirst : IComparer<JobRecord>
    {
        public static readonly NewestFirst Instance = new();

        /// <summary>The reverse, so a heap of the newest rows yields its oldest first.</summary>
        public static readonly IComparer<JobRecord> Oldest = Comparer<JobRecord>.Create((x, y) => Instance.Compare(y, x));

        public int Compare(JobRecord? x, JobRecord? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return 1;
            if (y is null) return -1;
            var bySubmitted = y.FirstSubmittedAt.CompareTo(x.FirstSubmittedAt);
            return bySubmitted != 0 ? bySubmitted : string.CompareOrdinal(y.Id, x.Id);
        }
    }
}
