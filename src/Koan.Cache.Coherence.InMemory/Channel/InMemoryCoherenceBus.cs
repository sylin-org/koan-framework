using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Koan.Cache.Abstractions.Coherence;

namespace Koan.Cache.Coherence.InMemory.Channel;

/// <summary>
/// In-process broadcast bus. Multiple <see cref="InMemoryCoherenceChannel"/> instances may
/// share a single bus to simulate cross-node delivery within one process — the primary
/// pattern for testing the cache pillar's coherence behaviour without a real transport.
/// </summary>
/// <remarks>
/// <para>
/// Thread-safe. Subscribers receive every published message (including the publisher's own);
/// the cache coordinator's origin filter is responsible for dropping echoes.
/// </para>
/// <para>
/// Publishing is best-effort: handler exceptions are swallowed and logged via the channel's
/// caller, matching the contract that coherence must never throw from <c>Publish</c>.
/// </para>
/// </remarks>
public sealed class InMemoryCoherenceBus
{
    private readonly List<Func<CacheInvalidation, CancellationToken, ValueTask>> _subscribers = new();
    private readonly object _lock = new();

    /// <summary>Subscribe a handler. Returns a disposable that unsubscribes when disposed.</summary>
    public IDisposable Subscribe(Func<CacheInvalidation, CancellationToken, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_lock) _subscribers.Add(handler);
        return new Subscription(this, handler);
    }

    /// <summary>Publish a message to every subscriber. Handler exceptions are swallowed (best-effort).</summary>
    public async ValueTask Publish(CacheInvalidation message, CancellationToken ct)
    {
        Func<CacheInvalidation, CancellationToken, ValueTask>[] snapshot;
        lock (_lock)
        {
            snapshot = _subscribers.ToArray();
        }

        foreach (var subscriber in snapshot)
        {
            try
            {
                await subscriber(message, ct).ConfigureAwait(false);
            }
            catch
            {
                // Coherence is best-effort by contract — never throw from Publish.
            }
        }
    }

    private void Unsubscribe(Func<CacheInvalidation, CancellationToken, ValueTask> handler)
    {
        lock (_lock) _subscribers.Remove(handler);
    }

    private sealed class Subscription : IDisposable
    {
        private readonly InMemoryCoherenceBus _bus;
        private readonly Func<CacheInvalidation, CancellationToken, ValueTask> _handler;
        private bool _disposed;

        public Subscription(InMemoryCoherenceBus bus, Func<CacheInvalidation, CancellationToken, ValueTask> handler)
        {
            _bus = bus;
            _handler = handler;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _bus.Unsubscribe(_handler);
        }
    }
}
