using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Koan.Core.Concurrency;

/// <summary>
/// Per-key <see cref="SemaphoreSlim"/> serialize-and-lease-timeout gate with refcounted gate
/// cleanup. Thread-safe; lives as a singleton in DI. See <see cref="IKeyedLeaseGate"/> for the
/// distinction from the in-flight coalescer <see cref="Koan.Core.Infrastructure.Singleflight"/>.
/// </summary>
internal sealed class KeyedLeaseGate : IKeyedLeaseGate
{
    private sealed class Gate
    {
        public Gate() => Semaphore = new SemaphoreSlim(1, 1);

        public SemaphoreSlim Semaphore { get; }
        public int RefCount;
    }

    private readonly ConcurrentDictionary<string, Gate> _gates = new(StringComparer.Ordinal);

    public async ValueTask<T> RunAsync<T>(
        string key,
        TimeSpan timeout,
        Func<CancellationToken, ValueTask<T>> action,
        CancellationToken ct)
    {
        var gate = _gates.GetOrAdd(key, _ => new Gate());
        Interlocked.Increment(ref gate.RefCount);
        try
        {
            ct.ThrowIfCancellationRequested();
            var effective = timeout <= TimeSpan.Zero ? TimeSpan.FromSeconds(5) : timeout;
            if (!await gate.Semaphore.WaitAsync(effective, ct).ConfigureAwait(false))
            {
                throw new TimeoutException($"Failed to acquire keyed lease for key '{key}' within {timeout}.");
            }

            try
            {
                return await action(ct).ConfigureAwait(false);
            }
            finally
            {
                gate.Semaphore.Release();
            }
        }
        finally
        {
            if (Interlocked.Decrement(ref gate.RefCount) == 0)
            {
                _gates.TryRemove(key, out _);
            }
        }
    }
}
