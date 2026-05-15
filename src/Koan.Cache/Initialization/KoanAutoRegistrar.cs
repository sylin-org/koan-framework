using System;
using System.Collections.Generic;
using System.Linq;
using Koan.Cache.Abstractions;
using Koan.Cache.Abstractions.Policies;
using Koan.Cache.Coherence;
using Koan.Cache.Diagnostics;
using Koan.Cache.Extensions;
using Koan.Cache.Options;
using Koan.Cache.Pillars;
using Koan.Cache.Topology;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Koan.Cache.Initialization;

/// <summary>
/// Koan.Cache pillar auto-registrar. Reference = Intent: pulling <c>Koan.Cache</c> into a
/// project wires the entire cache surface (Memory L1, layered orchestrator, coherence
/// coordinator stub, policy registry, decorator) automatically.
/// </summary>
public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Cache";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        CachingPillarManifest.EnsureRegistered();
        services.AddKoanCache();
    }

    public void Describe(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);

        var diagnosticsEnabled = Koan.Core.Configuration.ReadWithSource(cfg, CacheConstants.Configuration.EnableDiagnosticsEndpoint, true);
        var defaultRegion = Koan.Core.Configuration.ReadWithSource(cfg, CacheConstants.Configuration.DefaultRegionKey, CacheConstants.Configuration.DefaultRegion);
        var singleflightTimeout = Koan.Core.Configuration.ReadWithSource(cfg, CacheConstants.Configuration.DefaultSingleflightTimeout, TimeSpan.FromSeconds(5));

        module.AddSetting(
            "DefaultRegion",
            defaultRegion.Value ?? CacheConstants.Configuration.DefaultRegion,
            source: defaultRegion.Source,
            consumers: new[] { "Koan.Cache.RegionResolver" });

        module.AddSetting(
            "DiagnosticsEnabled",
            diagnosticsEnabled.Value.ToString(),
            source: diagnosticsEnabled.Source,
            consumers: new[] { "Koan.Cache.DiagnosticsEndpoint" });

        module.AddSetting(
            "DefaultSingleflightTimeout",
            singleflightTimeout.Value.ToString(),
            source: singleflightTimeout.Source,
            consumers: new[] { "Koan.Cache.Singleflight" });

        if (CacheTraceFilter.IsEnabled)
            module.AddSetting("TraceKey", CacheTraceFilter.TraceKey!, source: BootSettingSource.Environment);

        // Live topology / coherence / policy reporting — best-effort, depends on AppHost
        // being initialized. During early bootstrap these may not be available; we degrade
        // gracefully so Describe() never throws.
        TryDescribeTopology(module);
        TryDescribeCoherence(module);
        TryDescribePolicies(module);
    }

    private static void TryDescribeTopology(Koan.Core.Provenance.ProvenanceModuleWriter module)
    {
        try
        {
            var sp = Koan.Core.Hosting.App.AppHost.Current;
            if (sp is null) return;

            var topology = sp.GetService(typeof(CacheTopology)) as CacheTopology;
            if (topology is null) return;

            var summary = (topology.Local, topology.Remote) switch
            {
                ({ } local, { } remote) => $"layered (L1={local.Name}, L2={remote.Name})",
                ({ } local, null) => $"local-only (L1={local.Name})",
                (null, { } remote) => $"remote-only (L2={remote.Name})",
                _ => "none"
            };

            module.AddSetting("Topology", summary, source: BootSettingSource.Auto, consumers: new[] { "Koan.Cache.LayeredCache" });
        }
        catch
        {
            // Diagnostics are best-effort during bootstrap.
        }
    }

    private static void TryDescribeCoherence(Koan.Core.Provenance.ProvenanceModuleWriter module)
    {
        try
        {
            var sp = Koan.Core.Hosting.App.AppHost.Current;
            if (sp is null) return;

            var coordinator = sp.GetService(typeof(CoherenceCoordinator)) as CoherenceCoordinator;
            if (coordinator is null) return;

            var transports = coordinator.Channels.Count == 0
                ? "none"
                : string.Join(", ", coordinator.Channels.Select(c => $"{c.TransportName}{(c.Capabilities.SupportsCatchUp ? "+catchup" : "")}"));

            module.AddSetting(
                "Coherence",
                coordinator.IsActive
                    ? $"active (transports=[{transports}])"
                    : $"inactive (channels=[{transports}])",
                source: BootSettingSource.Auto,
                consumers: new[] { "Koan.Cache.CoherenceCoordinator" });

            module.AddSetting("NodeId", coordinator.NodeId.ToString("D"), source: BootSettingSource.Auto);
        }
        catch
        {
            // Best-effort.
        }
    }

    private static void TryDescribePolicies(Koan.Core.Provenance.ProvenanceModuleWriter module)
    {
        try
        {
            var sp = Koan.Core.Hosting.App.AppHost.Current;
            if (sp is null) return;

            var registry = sp.GetService(typeof(ICachePolicyRegistry)) as ICachePolicyRegistry;
            if (registry is null) return;

            var topology = sp.GetService(typeof(CacheTopology)) as CacheTopology;
            var coordinator = sp.GetService(typeof(CoherenceCoordinator)) as CoherenceCoordinator;

            var policies = registry.GetAllPolicies();
            module.AddSetting("Policies.Count", policies.Count.ToString(), source: BootSettingSource.Auto);

            foreach (var policy in policies)
            {
                var typeName = policy.DeclaringType?.Name ?? "Unknown";
                var line = DescribePolicy(policy, topology, coordinator);
                module.AddSetting($"Policy:{typeName}", line, source: BootSettingSource.Auto, consumers: new[] { "Koan.Cache.Decorator" });
            }
        }
        catch
        {
            // Best-effort.
        }
    }

    /// <summary>
    /// Build a one-line description of a policy including a health-flag suffix:
    /// <c>[OK]</c>, <c>[DEGRADED]</c> (e.g. Layered → effective LocalOnly because no Remote),
    /// or <c>[BUG]</c> (validation invariant violated — should be impossible since the
    /// materializer throws at boot).
    /// </summary>
    private static string DescribePolicy(CachePolicyDescriptor policy, CacheTopology? topology, CoherenceCoordinator? coordinator)
    {
        var parts = new List<string>
        {
            $"tier={policy.Tier}",
            $"ttl={Fmt(policy.AbsoluteTtl)}",
            $"l1={Fmt(policy.L1AbsoluteTtl)}",
            $"strategy={policy.Strategy}",
            $"tags=[{string.Join(",", policy.Tags)}]",
            $"broadcast={(policy.ForceCoherenceBroadcast ? "yes" : "no")}",
        };

        var health = ComputeHealth(policy, topology, coordinator);
        return $"{string.Join(", ", parts)} [{health}]";
    }

    private static string ComputeHealth(CachePolicyDescriptor policy, CacheTopology? topology, CoherenceCoordinator? coordinator)
    {
        // BUG: L1 > L2 (materializer rejects this at boot, so it should never appear here;
        // included for completeness in case future code paths skip the validator).
        if (policy.AbsoluteTtl is { } abs && policy.L1AbsoluteTtl is { } l1 && l1 > abs)
            return "BUG: L1Ttl > AbsoluteTtl";

        if (topology is null) return "OK";

        // DEGRADED: Tier=Layered but no Remote tier resolved
        if (policy.Tier == Abstractions.Primitives.CacheTier.Layered && topology.Remote is null)
            return "DEGRADED: Layered → LocalOnly (no Remote tier registered)";

        // DEGRADED: RemoteOnly but no Remote tier
        if (policy.Tier == Abstractions.Primitives.CacheTier.RemoteOnly && topology.Remote is null)
            return "DEGRADED: RemoteOnly but no Remote tier registered";

        // DEGRADED: broadcast requested but no coherence channel active
        if (policy.ForceCoherenceBroadcast && coordinator is { IsActive: false })
            return "DEGRADED: broadcast=yes but coherence inactive";

        return "OK";
    }

    private static string Fmt(TimeSpan? ts) => ts.HasValue ? $"{ts.Value.TotalSeconds:0.##}s" : "-";
}
