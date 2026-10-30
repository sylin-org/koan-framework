namespace Koan.Jobs;

/// <summary>
/// What waiting on a job learned, returned by <see cref="JobHandle.Completion"/> (JOBS-0010). It is exactly one of three
/// cases, told apart by type: <see cref="Settled"/>, the job reached a terminal status; <see cref="TimedOut"/>, the wait
/// ended first, with the status the job had reached; <see cref="Unknown"/>, nobody can read the outcome any more. A
/// submission coalesced into another job after it queued is answered by the job that serves it, which the outcome
/// names in <c>ServedBy</c>.
/// </summary>
public abstract record JobOutcome
{
    private protected JobOutcome()
    {
    }

    /// <summary>True when the job, or the job that served it, settled <see cref="JobStatus.Completed"/>.</summary>
    public bool Succeeded => this is Settled { Status: JobStatus.Completed };

    /// <summary>The job settled with <paramref name="Status"/> and its last error. <paramref name="ServedBy"/> names the
    /// job that settled in its place when the submission was coalesced into another job; null when the job itself settled.</summary>
    public sealed record Settled(JobStatus Status, string? Error, string? ServedBy = null) : JobOutcome;

    /// <summary>The wait ended before the job settled; <paramref name="Status"/> is the status it had reached, of the job
    /// <paramref name="ServedBy"/> names when another job serves the submission.</summary>
    public sealed record TimedOut(JobStatus Status, string? ServedBy = null) : JobOutcome;

    /// <summary>The ledger no longer holds the job and this process did not hear it settle: it was never submitted, or it
    /// settled and was evicted or purged before its outcome was read. Never reported as a failure it was not.</summary>
    public sealed record Unknown(string Reason) : JobOutcome;
}

/// <summary>
/// A handle to a submitted job. <see cref="Completion"/> waits for the job's outcome up to the caller's timeout — bounded
/// because a durable job can run for minutes or on another node. It reads the ledger, the single truth a waiter shares
/// with every node, on a short interval, and hears at once a settlement this process makes; it follows a submission
/// coalesced into another job to the job that serves it. JOBS-0005 §4.5, JOBS-0010.
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

    /// <summary>Wait for the job's outcome: <see cref="JobOutcome.Settled"/> when it settles in time,
    /// <see cref="JobOutcome.TimedOut"/> with the status it reached when it does not, <see cref="JobOutcome.Unknown"/> when
    /// nobody can read its outcome any more.</summary>
    public Task<JobOutcome> Completion(TimeSpan timeout, CancellationToken ct = default) => _completion(timeout, ct);
}
