using System;
using System.Threading;
using System.Threading.Tasks;

namespace Koan.Core.Concurrency;

/// <summary>
/// Per-key serialize-and-lease-timeout concurrency gate. Callers that share the same string key
/// are admitted <b>one at a time</b> (FIFO via a per-key <see cref="SemaphoreSlim"/>); each admitted
/// caller runs its own action, and a caller that cannot acquire the lease within its timeout window
/// fails with <see cref="TimeoutException"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is <b>not</b> singleflight/coalescing. Every caller's action executes (serially); the gate
/// only bounds concurrency to one-per-key and bounds the wait. Contrast with the in-flight
/// <i>coalescer</i> <see cref="Koan.Core.Infrastructure.Singleflight"/>, where one execution is
/// shared by all concurrent callers of a key and there is no lease timeout. Pick this gate when each
/// caller must do its own work but only one at a time per key (e.g. cache fill with a bounded wait);
/// pick the coalescer when the work is identical and a single result should fan out to all callers.
/// </para>
/// <para>
/// Generic across the framework — used by the cache pillar to bound concurrent cache fills, but
/// equally applicable to AI embedding computations, heavy database queries, file operations, and
/// slow upstream calls. Register via <c>services.AddKoanKeyedLeaseGate()</c>.
/// </para>
/// <para>
/// Implementations are expected to be process-local and thread-safe. There is no cross-node gating
/// in this contract.
/// </para>
/// </remarks>
public interface IKeyedLeaseGate
{
    /// <summary>
    /// Run <paramref name="action"/> under exclusive lease for <paramref name="key"/>. The first
    /// caller executes immediately; subsequent callers with the same key wait their turn (up to
    /// <paramref name="timeout"/>) and then run their own action.
    /// </summary>
    /// <param name="key">Unique identifier of the work item.</param>
    /// <param name="timeout">Maximum time to wait for the lease. <c>TimeSpan.Zero</c> or negative uses a 5s default.</param>
    /// <param name="action">The work to perform.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="TimeoutException">Lease could not be acquired within <paramref name="timeout"/>.</exception>
    ValueTask<T> RunAsync<T>(
        string key,
        TimeSpan timeout,
        Func<CancellationToken, ValueTask<T>> action,
        CancellationToken ct);
}
