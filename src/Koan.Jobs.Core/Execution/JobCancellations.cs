using System.Collections.Concurrent;

namespace Koan.Jobs.Execution;

/// <summary>
/// Ephemeral, in-memory cancellation registry (JOBS-0003). Replaces the old per-entry flag on the
/// store index. A cancel request marks the job id; the dispatcher honours it at dispatch and the
/// progress tracker surfaces it to running jobs. Lost on restart by design (a persisted job that
/// was mid-run is reconciled by boot recovery, not by an in-memory flag).
/// </summary>
internal sealed class JobCancellations
{
    private readonly ConcurrentDictionary<string, byte> _requested = new();

    public void Request(string jobId) => _requested[jobId] = 0;
    public bool IsRequested(string jobId) => _requested.ContainsKey(jobId);
    public void Clear(string jobId) => _requested.TryRemove(jobId, out _);
}
