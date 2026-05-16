using System;
using System.Threading;
using System.Threading.Tasks;
using Koan.Cache.Abstractions.Coherence;

namespace Koan.Tests.Cache.Coherence.InMemory.Support;

/// <summary>
/// Test channel that throws from <see cref="Subscribe"/>. Models a transport whose
/// connection (e.g., Redis pub/sub) is unreachable at host-startup time.
/// </summary>
internal sealed class FailingCoherenceChannel : ICacheCoherenceChannel
{
    private readonly Exception _subscribeError;

    public FailingCoherenceChannel(string transportName, Exception? subscribeError = null)
    {
        TransportName = transportName;
        _subscribeError = subscribeError ?? new InvalidOperationException($"Simulated {transportName} subscribe failure.");
    }

    public string TransportName { get; }

    public CoherenceCapabilities Capabilities { get; } = CoherenceCapabilities.BestEffort;

    public ValueTask Publish(CacheInvalidation message, CancellationToken ct) => ValueTask.CompletedTask;

    public ValueTask Subscribe(Func<CacheInvalidation, CancellationToken, ValueTask> onReceived, CancellationToken ct)
        => throw _subscribeError;

    public ValueTask<string?> CatchUp(string? cursor, Func<CacheInvalidation, CancellationToken, ValueTask> onReceived, CancellationToken ct)
        => ValueTask.FromResult<string?>(null);
}
