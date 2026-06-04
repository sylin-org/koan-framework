namespace Koan.Jobs;

/// <summary>Terminal outcome of a job, returned by <see cref="JobHandle.Completion"/>.</summary>
public sealed record JobOutcome(JobStatus Status, string? Error)
{
    public bool Succeeded => Status == JobStatus.Completed;
}

/// <summary>
/// A handle to a submitted job. <see cref="Completion"/> awaits the terminal state (ledger-polled; a push signal
/// when a bus is present) — bounded by a timeout because a durable job can run for minutes / cross-process.
/// JOBS-0005 §4.5.
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
