namespace Koan.Jobs;

/// <summary>What a declared <c>[JobChain]</c> does when a step fails after exhausting retries (JOBS-0005 §6.5).</summary>
public enum OnFailure
{
    /// <summary>Stop the chain; the job is terminal (default — "Parse failed → don't Mint garbage").</summary>
    Abort = 0,
    /// <summary>Proceed to the next chain stage despite this step's failure (opt-in).</summary>
    Continue = 1,
}

/// <summary>Why a job reached the terminal <see cref="JobStatus.Dead"/> state.</summary>
public enum DeadReason
{
    /// <summary>Retries exhausted on a failing handler with no chain-continue.</summary>
    Poison = 0,
    /// <summary>Reschedule/backoff hit the <c>Deadline</c> or <c>MaxReschedules</c> guard.</summary>
    PerpetuallyDeferred = 1,
    /// <summary>The Core context carrier bag captured at submit could not be restored at execute (for example, an
    /// unregistered axis or unsupported format) — deterministic, so dead-lettered rather than run fail-open.</summary>
    CarrierRestoreFailed = 2,
}

/// <summary>The control signal a handler raised via <see cref="JobContext"/> verbs, read by the orchestrator post-execute.
/// Exposed publicly so integration tests can assert the exact signal without querying the ledger.</summary>
public enum JobSignal
{
    None = 0,
    Reschedule = 1,
    Backoff = 2,
    ContinueWith = 3,
    StopChain = 4,
}
