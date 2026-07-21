using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Koan.Cache.Abstractions.Primitives;
using Koan.Cache.Coherence;
using Koan.Cache.Topology;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Koan.Cache.Diagnostics;

/// <summary>
/// <see cref="IHealthCheck"/> for the cache pillar. Reports L1 / L2 reachability and the
/// coherence coordinator's activation status, aggregating to <c>Healthy</c> /
/// <c>Degraded</c> / <c>Unhealthy</c>. Kubernetes/Aspire readiness probes pick it up
/// automatically via <c>MapHealthChecks("/health")</c>.
/// </summary>
/// <remarks>
/// <para>
/// Each configured tier gets a sentinel write+read round-trip. Failures are tolerated:
/// L2 unreachable while L1 works = <c>Degraded</c> (cache still functional, just not
/// distributed); both unreachable = <c>Unhealthy</c>.
/// </para>
/// <para>
/// Peer-invalidation state is informational in AutoDetect; local-only and remote-only topologies
/// need no cross-node L1 eviction and are healthy with the coordinator inactive.
/// </para>
/// </remarks>
public sealed class CacheHealthCheck : IHealthCheck
{
    private static readonly CacheKey SentinelKey = new("koan.cache.healthcheck");
    private static readonly CacheValue SentinelValue = CacheValue.FromString("ok");
    private static readonly CacheWriteOptions SentinelWrite = CacheWriteOptions.Default with
    {
        AbsoluteTtl = TimeSpan.FromSeconds(10),
        ForceCoherenceBroadcast = false,
    };

    private readonly LayeredCache _layered;
    private readonly CoherenceCoordinator _coordinator;
    private readonly ILogger<CacheHealthCheck> _logger;

    internal CacheHealthCheck(LayeredCache layered, CoherenceCoordinator coordinator, ILogger<CacheHealthCheck> logger)
    {
        _layered = layered ?? throw new ArgumentNullException(nameof(layered));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object>(StringComparer.Ordinal);

        var topology = _layered.Topology;
        data["topology.local"] = topology.Local?.Name ?? "none";
        data["topology.remote"] = topology.Remote?.Name ?? "none";
        data["coherence.active"] = _coordinator.IsActive;
        data["coherence.provider"] = _coordinator.ProviderId;
        data["coherence.assurance"] = _coordinator.Assurance;
        data["node.id"] = _coordinator.NodeId.ToString("D");

        var localOk = await ProbeStore(topology.Local, "local", data, cancellationToken).ConfigureAwait(false);
        var remoteOk = await ProbeStore(topology.Remote, "remote", data, cancellationToken).ConfigureAwait(false);

        // If no tiers configured → Unhealthy (cache wired but inoperable)
        if (topology.Local is null && topology.Remote is null)
            return HealthCheckResult.Unhealthy("Koan.Cache has no tiers registered.", data: data);

        // Both configured tiers must reach Healthy for Healthy overall.
        var localExpected = topology.Local is not null;
        var remoteExpected = topology.Remote is not null;

        if (localExpected && !localOk && remoteExpected && !remoteOk)
            return HealthCheckResult.Unhealthy("Both cache tiers unreachable.", data: data);

        if ((localExpected && !localOk) || (remoteExpected && !remoteOk))
            return HealthCheckResult.Degraded("One cache tier unreachable — see data for which.", data: data);

        return HealthCheckResult.Healthy("Koan.Cache: all configured tiers reachable.", data);
    }

    private async Task<bool> ProbeStore(Koan.Cache.Abstractions.Stores.ICacheStore? store, string tierTag, Dictionary<string, object> data, CancellationToken ct)
    {
        if (store is null)
        {
            data[$"{tierTag}.status"] = "not-configured";
            return true; // not configured = not a failure
        }

        try
        {
            var probeKey = new CacheKey(SentinelKey.Value + ":" + tierTag);
            await store.Set(probeKey, SentinelValue, SentinelWrite, ct).ConfigureAwait(false);
            var exists = await store.Exists(probeKey, ct).ConfigureAwait(false);
            await store.Remove(probeKey, ct).ConfigureAwait(false);

            data[$"{tierTag}.status"] = exists ? "reachable" : "set-but-not-readable";
            return exists;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Koan.Cache health probe failed for {Tier} tier ({Store}).", tierTag, store.Name);
            data[$"{tierTag}.status"] = "error";
            data[$"{tierTag}.error"] = ex.GetType().Name + ": " + ex.Message;
            return false;
        }
    }
}
