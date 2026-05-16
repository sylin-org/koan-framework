namespace Koan.Jobs.Model;

public enum JobStatus
{
    Created = 0,
    Queued = 10,
    Running = 20,

    /// <summary>
    /// Logically queued, but holding for a declared dependency to clear. The job is in the
    /// queue and will be re-picked on the next sweep — no work is in flight, no host-rate-gate
    /// is being consumed. Transitions back to <see cref="Queued"/> (and then <see cref="Running"/>)
    /// when dependencies clear, or to <see cref="Failed"/> when a specific-id dependency ends
    /// <see cref="Failed"/> / <see cref="Cancelled"/>. See ADR-0017.
    /// </summary>
    Blocked = 30,

    Completed = 100,
    Failed = 110,
    Cancelled = 120
}
