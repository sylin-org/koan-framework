namespace Koan.Jobs;

/// <summary>
/// Settlements this process makes, delivered to the handles waiting on them (JOBS-0010). A handle reads its job's
/// outcome from the ledger, but a ledger may no longer hold a settled job — the in-memory ledger evicts its oldest
/// settled jobs, and retention purges durable ones — so a waiter also hears the settlement itself when this process
/// made it. The ledger tells this before it can evict the settled row.
/// </summary>
internal sealed class JobCompletions
{
    private readonly object _gate = new();
    private readonly Dictionary<string, List<Watch>> _watching = new(StringComparer.Ordinal);

    /// <summary>Start hearing <paramref name="jobId"/>'s settlement; dispose to stop.</summary>
    public Watch Hear(string jobId)
    {
        var watch = new Watch(this, jobId);
        lock (_gate)
        {
            if (!_watching.TryGetValue(jobId, out var watchers)) _watching[jobId] = watchers = [];
            watchers.Add(watch);
        }
        return watch;
    }

    /// <summary>A row this process wrote: a settled row completes its waiters; any other changes nothing.</summary>
    public void Settled(JobRecord record)
    {
        if (!record.IsTerminal) return;
        lock (_gate)
        {
            if (!_watching.Remove(record.Id, out var watchers)) return;
            foreach (var watch in watchers) watch.Complete(new JobOutcome(record.Status, record.LastError));
        }
    }

    private void Release(Watch watch)
    {
        lock (_gate)
        {
            if (!_watching.TryGetValue(watch.JobId, out var watchers)) return;
            watchers.Remove(watch);
            if (watchers.Count == 0) _watching.Remove(watch.JobId);
        }
    }

    /// <summary>One waiter's subscription.</summary>
    internal sealed class Watch(JobCompletions owner, string jobId) : IDisposable
    {
        private readonly TaskCompletionSource<JobOutcome> _heard = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string JobId { get; } = jobId;

        /// <summary>Completes when the job settles in this process.</summary>
        public Task<JobOutcome> Heard => _heard.Task;

        internal void Complete(JobOutcome outcome) => _heard.TrySetResult(outcome);

        public void Dispose() => owner.Release(this);
    }
}
