namespace Koan.Jobs;

/// <summary>Terminal outcome of a job, returned by <see cref="JobHandle.Completion"/>.</summary>
public sealed record JobOutcome(JobStatus Status, string? Error)
{
    public bool Succeeded => Status == JobStatus.Completed;
}

/// <summary>
/// A handle to a submitted job. <see cref="Completion"/> awaits terminal state by polling the ledger on a short
/// interval up to the caller's timeout — bounded because a durable job can run for minutes or cross-process, and
/// no push signal exists by design: hints hurry claims, handlers settle rows, and the ledger remains the single
/// truth a waiter reads. JOBS-0005 §4.5.
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

    /// <summary>Await the terminal outcome, or a <see cref="JobStatus"/>-less timeout outcome if it doesn't settle in time.</summary>
    public Task<JobOutcome> Completion(TimeSpan timeout, CancellationToken ct = default) => _completion(timeout, ct);
}
