namespace Koan.Jobs;

/// <summary>
/// What waiting on a job learned, returned by <see cref="JobHandle.Completion"/> (JOBS-0010): the status the job has
/// reached, and its last error when it settled. A status that is not terminal means the wait ended first. No status means
/// nobody can read the outcome any more, and <paramref name="Error"/> says why; that is never reported as a failure.
/// </summary>
public sealed record JobOutcome(JobStatus? Status, string? Error = null)
{
    /// <summary>True when the job settled <see cref="JobStatus.Completed"/>.</summary>
    public bool Succeeded => Status == JobStatus.Completed;

    /// <summary>True when the job reached a terminal status.</summary>
    public bool Settled => Status is JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled or JobStatus.Dead;
}

/// <summary>
/// A handle to a submitted job. <see cref="Completion"/> waits for the job's outcome up to the caller's timeout — bounded
/// because a durable job can run for minutes or on another node. It reads the ledger, the single truth a waiter shares
/// with every node, on a short interval, and hears at once a settlement this process makes. JOBS-0005 §4.5, JOBS-0010.
/// </summary>
public sealed class JobHandle
{
    private readonly Func<TimeSpan, CancellationToken, Task<JobOutcome>> _completion;

    internal JobHandle(string jobId, Func<TimeSpan, CancellationToken, Task<JobOutcome>> completion)
    {
        JobId = jobId;
        _completion = completion;
    }

    /// <summary>The ledger entry id.</summary>
    public string JobId { get; }

    /// <summary>Wait for the job's outcome: its terminal status when it settles in time, the status it reached when it does
    /// not, and no status when nobody can read its outcome any more.</summary>
    public Task<JobOutcome> Completion(TimeSpan timeout, CancellationToken ct = default) => _completion(timeout, ct);
}
