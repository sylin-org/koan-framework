using System;
using System.Threading;
using System.Threading.Tasks;
using Koan.Cache.Abstractions.Coherence;
using Koan.Data.Abstractions;

namespace Koan.Cache.Coherence.InMemory.Channel;

/// <summary>
/// In-process <see cref="ICacheCoherenceChannel"/> implementation. Attaches to a shared
/// <see cref="InMemoryCoherenceBus"/>; multiple channels on the same bus simulate cross-node
/// delivery within one process. Lowest priority so any real transport (Redis-pubsub, Koan.Messaging)
/// preempts when both are registered.
/// </summary>
[ProviderPriority(int.MinValue)]
public sealed class InMemoryCoherenceChannel : ICacheCoherenceChannel, IDisposable
{
    private readonly InMemoryCoherenceBus _bus;
    private IDisposable? _subscription;

    public InMemoryCoherenceChannel(InMemoryCoherenceBus bus)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
    }

    public string TransportName => "in-memory";

    public CoherenceCapabilities Capabilities { get; } = CoherenceCapabilities.BestEffort;

    public ValueTask Publish(CacheInvalidation invalidation, CancellationToken ct)
        => _bus.Publish(invalidation, ct);

    public ValueTask Subscribe(Func<CacheInvalidation, CancellationToken, ValueTask> onReceived, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(onReceived);
        _subscription = _bus.Subscribe(onReceived);
        return ValueTask.CompletedTask;
    }

    /// <summary>No-op — the in-memory bus has no durability or replay.</summary>
    public ValueTask<string?> CatchUp(
        string? cursor,
        Func<CacheInvalidation, CancellationToken, ValueTask> onReceived,
        CancellationToken ct)
        => ValueTask.FromResult<string?>(null);

    public void Dispose() => _subscription?.Dispose();
}
