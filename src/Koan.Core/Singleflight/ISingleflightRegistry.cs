using System;
using System.Threading;
using System.Threading.Tasks;

namespace Koan.Core.Singleflight;

/// <summary>
/// Stampede-protection primitive. Coalesces concurrent calls keyed on the same string into a
/// single execution; the rest of the callers await the same outcome.
/// </summary>
/// <remarks>
/// <para>
/// Generic across the framework — used by the cache pillar to prevent thundering-herd cache
/// fills, but equally applicable to AI embedding computations, heavy database queries, file
/// operations, and slow upstream calls. Register via <c>services.AddKoanSingleflight()</c>.
/// </para>
/// <para>
/// Implementations are expected to be process-local and thread-safe. There is no cross-node
/// singleflight in this contract.
/// </para>
/// </remarks>
public interface ISingleflightRegistry
{
    /// <summary>
    /// Run <paramref name="action"/> under exclusive lease for <paramref name="key"/>. The first
    /// caller executes; subsequent callers with the same key await the first's completion.
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
