namespace Koan.Jobs;

/// <summary>
/// Settlements this process makes, delivered to the handles waiting on them (JOBS-0010). A handle reads its job's
/// outcome from the ledger, but a ledger may no longer hold a settled job — the in-memory ledger evicts its oldest
/// settled jobs, and retention purges durable ones — so a waiter also hears the settlement itself when this process
/// made it. The ledger tells this before it stores the settled row, so a settlement is always heard before the row can
/// be evicted. A submission coalesced into another job after it queued settles with <see cref="JobRecord.ServedBy"/>:
/// its waiters move on to the job that serves it and hear that job's settlement.
/// </summary>
internal sealed class JobCompletions
{
    private readonly object _gate = new();
    private readonly Dictionary<string, List<Watch>> _watching = new(StringComparer.Ordinal);

    /// <summary>Start hearing <paramref name="jobId"/>'s settlement; dispose to stop.</summary>
    public Watch Hear(string jobId)
    {
        var watch = new Watch(this, jobId);
        lock (_gate) Add(jobId, watch);
        return watch;
    }

    /// <summary>A row this process wrote: a settled row completes its waiters, and a row coalesced into another job hands
    /// them on to the job that serves it. A row that has not settled changes nothing.</summary>
    public void Settled(JobRecord record)
    {
        if (!record.IsTerminal) return;
        lock (_gate)
        {
            if (!_watching.Remove(record.Id, out var watchers)) return;
            foreach (var watch in watchers)
            {
                if (record.ServedBy is { } serving)
                {
                    watch.Following = serving;
                    Add(serving, watch);
                }
                else
                {
                    watch.Complete(new Settlement(record.Id, record.Status, record.LastError));
                }
            }
        }
    }

    private void Follow(Watch watch, string from, string to)
    {
        lock (_gate)
        {
            if (watch.Heard.IsCompleted || !string.Equals(watch.Following, from, StringComparison.Ordinal)) return;
            Remove(from, watch);
            watch.Following = to;
            Add(to, watch);
        }
    }

    private void Release(Watch watch)
    {
        lock (_gate) Remove(watch.Following, watch);
    }

    private void Add(string jobId, Watch watch)
    {
        if (!_watching.TryGetValue(jobId, out var watchers)) _watching[jobId] = watchers = [];
        watchers.Add(watch);
    }

    private void Remove(string jobId, Watch watch)
    {
        if (!_watching.TryGetValue(jobId, out var watchers)) return;
        watchers.Remove(watch);
        if (watchers.Count == 0) _watching.Remove(jobId);
    }

    /// <summary>What a watch heard: the job that settled — the watched one, or the job that served it — and how.</summary>
    internal readonly record struct Settlement(string JobId, JobStatus Status, string? Error);

    /// <summary>One waiter's subscription. <see cref="Following"/> is the job it currently listens to.</summary>
    internal sealed class Watch : IDisposable
    {
        private readonly JobCompletions _owner;
        private readonly TaskCompletionSource<Settlement> _heard = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private volatile string _following;

        internal Watch(JobCompletions owner, string jobId)
        {
            _owner = owner;
            _following = jobId;
        }

        /// <summary>The job this watch listens to: the watched job, or the job that serves it.</summary>
        public string Following
        {
            get => _following;
            internal set => _following = value;
        }

        /// <summary>Completes when the job this watch follows settles in this process.</summary>
        public Task<Settlement> Heard => _heard.Task;

        /// <summary>Listen to <paramref name="to"/>, which serves <paramref name="from"/>, unless the watch already moved on.</summary>
        public void Follow(string from, string to) => _owner.Follow(this, from, to);

        internal void Complete(Settlement heard) => _heard.TrySetResult(heard);

        public void Dispose() => _owner.Release(this);
    }
}
