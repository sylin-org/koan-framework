namespace Koan.Jobs;

/// <summary>
/// Push-dispatch seam (JOBS-0005 — the <c>+bus</c> tier). Instead of polling on a fixed interval, the worker
/// awaits <see cref="WaitForWork"/>; the coordinator calls <see cref="Notify"/> after a submit to wake it
/// immediately. The default in-process transport coalesces signals within one node; the messaging transport
/// (<c>Koan.Jobs.Transport.Messaging</c>) extends the wake across nodes.
/// </summary>
/// <remarks>
/// The ledger is always the truth — a signal is a latency hint, never a source of work. A missed or duplicated
/// signal is harmless: the worker still polls at <c>PollInterval</c>, so the worst case is one interval of added
/// dispatch latency. This keeps the delivery contract (at-least-once + idempotent) constant whether or not a
/// transport is wired.
/// </remarks>
public interface IJobTransport
{
    /// <summary>Signal that work may be available (called after a submit/trigger). Coalescing and best-effort.</summary>
    void Notify();

    /// <summary>Block until a <see cref="Notify"/> arrives or <paramref name="timeout"/> elapses (the poll fallback).</summary>
    Task WaitForWork(TimeSpan timeout, CancellationToken ct);
}
