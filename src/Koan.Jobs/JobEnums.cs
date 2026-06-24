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
    /// <summary>ARCH-0100: the ambient carrier captured at submit could not be rehydrated at execute (an
    /// unregistered axis, or an unknown carrier format) — deterministic, so dead-lettered rather than run fail-open.</summary>
    CarrierRestoreFailed = 2,
}

/// <summary>How the durable tier secures a ready job under competing consumers (JOBS-0005 §7).</summary>
public enum ClaimStrategy
{
    /// <summary>Read a candidate and write Running (last-write-wins). Correct under at-least-once + idempotent; cheapest.
    /// Right for single-node / low-contention. Default.</summary>
    Optimistic = 0,

    /// <summary>Leaderless GUIDv7 "bakery" election: write a claim ticket to a parallel set, reserve a window, the
    /// smallest GUIDv7 (time-ordered) wins. Adapter-generic (only Upsert+Query), no native CAS needed. Probabilistic:
    /// needs NTP and a window &gt; clock-skew + write-propagation; idempotency remains the backstop. A proper
    /// consensus/sync module is a future Koan primitive (the hard-guarantee tier).</summary>
    Ticket = 1,
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
