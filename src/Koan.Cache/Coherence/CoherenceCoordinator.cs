using Koan.Cache.Abstractions.Coherence;
using Koan.Cache.Abstractions.Primitives;
using Koan.Cache.Options;
using Koan.Cache.Topology;
using Koan.Communication.Signals;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Cache.Coherence;

/// <summary>
/// Owns the Cache meaning of peer invalidation. Communication carries one best-effort signal to every active
/// node; Cache filters the writer and evicts only the receiving node's L1. L1 TTL remains the correctness bound.
/// </summary>
internal sealed class CoherenceCoordinator(
    NodeIdProvider nodeIdProvider,
    LayeredCache layered,
    IFrameworkSignalPublisher signals,
    IOptionsMonitor<CacheOptions> options,
    ILogger<CoherenceCoordinator> logger)
    : IHostedService, IHandleFrameworkBroadcast<CacheInvalidationSignal>
{
    private bool _active;

    public Guid NodeId => nodeIdProvider.NodeId;
    public bool IsActive => _active;
    public string ProviderId => signals.BroadcastProviderId;
    public string Assurance => signals.BroadcastAssurance;

    public Task StartAsync(CancellationToken ct)
    {
        var mode = options.CurrentValue.CoherenceMode;
        if (mode == CoherenceMode.Disabled)
        {
            logger.LogInformation("Koan Cache coherence: disabled by configuration.");
            return Task.CompletedTask;
        }

        if (!layered.Topology.IsLayered)
        {
            logger.LogInformation(
                "Koan Cache coherence: inactive because topology is not layered (L1={Local}, L2={Remote}).",
                layered.Topology.Local?.Name ?? "none",
                layered.Topology.Remote?.Name ?? "none");
            return Task.CompletedTask;
        }

        if (mode == CoherenceMode.Required && signals.BroadcastIsBuiltIn)
        {
            throw new InvalidOperationException(
                "Cache coherence is Required for a layered topology, but Communication elected only its " +
                "process-local FrameworkBroadcasts provider. Reference a node-broadcast provider such as " +
                "Koan.Cache.Adapter.Redis or Koan.Communication.Connector.RabbitMq, or use AutoDetect/Disabled.");
        }

        _active = true;
        logger.LogInformation(
            "Koan Cache coherence: active, provider={Provider}, assurance={Assurance}, " +
            "receiver=L1-only, safety=L1-TTL.",
            ProviderId,
            Assurance);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        _active = false;
        return Task.CompletedTask;
    }

    public ValueTask BroadcastEvict(CacheKey key, CancellationToken ct)
    {
        if (!_active || ct.IsCancellationRequested) return ValueTask.CompletedTask;

        if (!signals.TryBroadcast(new CacheInvalidationSignal(
                key.Value,
                NodeId)))
        {
            logger.LogDebug(
                "Koan Cache could not enqueue peer invalidation for {Key}; L1 TTL remains the staleness bound.",
                key.Value);
        }

        return ValueTask.CompletedTask;
    }

    public async ValueTask Handle(CacheInvalidationSignal signal, CancellationToken ct)
    {
        if (!_active || signal.OriginNodeId == NodeId || string.IsNullOrWhiteSpace(signal.Key)) return;

        try
        {
            await layered.EvictLocal(new CacheKey(signal.Key), ct).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            logger.LogWarning(
                error,
                "Koan Cache could not apply peer invalidation for {Key}; L1 TTL remains the staleness bound.",
                signal.Key);
        }
    }
}

internal readonly record struct CacheInvalidationSignal(
    string Key,
    Guid OriginNodeId)
    : IFrameworkBroadcast<CacheInvalidationSignal>
{
    public static string ContractId => "koan.cache.peer-invalidation@1";
}
