using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Koan.Core.Singleflight;

/// <summary>
/// Per-key semaphore singleflight implementation with refcounted gate cleanup.
/// Thread-safe; lives as a singleton in DI.
/// </summary>
internal sealed class SingleflightRegistry : ISingleflightRegistry
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
                throw new TimeoutException($"Failed to acquire singleflight lock for key '{key}' within {timeout}.");
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
