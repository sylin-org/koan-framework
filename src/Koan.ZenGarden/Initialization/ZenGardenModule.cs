using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Orchestration.Composition;
using Koan.Core.Provenance;
using Koan.Core.Semantics;
using Koan.Core.Semantics.Contributions;
using Koan.ZenGarden.Extensions;
using Koan.ZenGarden.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Koan.ZenGarden.Initialization;

internal sealed class ZenGardenModule : KoanModule, IContributeTo<DiscoveryContributionTarget>
{
    public override void Register(IServiceCollection services)
    {
        services.AddZenGardenRuntime();
    }

    public void Contribute(DiscoveryContributionTarget target) =>
        target.AddSource<Discovery.ZenGardenDiscoverySource>(
            Constants.Composition.SourceId,
            Constants.Composition.IntentScheme);

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        module.AddNote("Layered discovery source: active; adapter service names and aliases become health-checked automatic candidates");

        var defaults = new ZenGardenOptions();
        var endpoint = Configuration.ReadWithSource(cfg, ConfigurationConstants.FullKey(ConfigurationConstants.Keys.Endpoint), defaults.Endpoint);
        var enableDiscovery = Configuration.ReadWithSource(cfg, ConfigurationConstants.FullKey(ConfigurationConstants.Keys.EnableDiscovery), defaults.EnableDiscovery);
        var discoveryTimeoutSeconds = Configuration.ReadWithSource(cfg, ConfigurationConstants.FullKey(ConfigurationConstants.Keys.DiscoveryTimeoutSeconds), defaults.DiscoveryTimeoutSeconds);
        var discoveryPort = Configuration.ReadWithSource(cfg, ConfigurationConstants.FullKey(ConfigurationConstants.Keys.DiscoveryPort), defaults.DiscoveryPort);
        var discoveryGroup = Configuration.ReadWithSource(cfg, ConfigurationConstants.FullKey(ConfigurationConstants.Keys.DiscoveryMulticastGroup), defaults.DiscoveryMulticastGroup);
        var discoveryCacheTtl = Configuration.ReadWithSource(cfg, ConfigurationConstants.FullKey(ConfigurationConstants.Keys.DiscoveryCacheTtlSeconds), defaults.DiscoveryCacheTtlSeconds);
        var discoveryBroadcastFallback = Configuration.ReadWithSource(cfg, ConfigurationConstants.FullKey(ConfigurationConstants.Keys.DiscoveryEnableBroadcastFallback), defaults.DiscoveryEnableBroadcastFallback);
        var discoveryLimitedBroadcast = Configuration.ReadWithSource(cfg, ConfigurationConstants.FullKey(ConfigurationConstants.Keys.DiscoveryEnableLimitedBroadcast), defaults.DiscoveryEnableLimitedBroadcast);
        var timeoutSeconds = Configuration.ReadWithSource(cfg, ConfigurationConstants.FullKey(ConfigurationConstants.Keys.HttpTimeoutSeconds), defaults.HttpTimeoutSeconds);
        var reconnectSeconds = Configuration.ReadWithSource(cfg, ConfigurationConstants.FullKey(ConfigurationConstants.Keys.StreamReconnectDelaySeconds), defaults.StreamReconnectDelaySeconds);
        var dedupeWindow = Configuration.ReadWithSource(cfg, ConfigurationConstants.FullKey(ConfigurationConstants.Keys.DedupeWindowSize), defaults.DedupeWindowSize);
        var requireHostMoss = Configuration.ReadWithSource(cfg, ConfigurationConstants.FullKey(ConfigurationConstants.Keys.RequireHostMossWhenContainerized), defaults.RequireHostMossWhenContainerized);
        var containerHost = Configuration.ReadWithSource(cfg, ConfigurationConstants.FullKey(ConfigurationConstants.Keys.ContainerHost), defaults.ContainerHost);
        var containerHostPort = Configuration.ReadWithSource(cfg, ConfigurationConstants.FullKey(ConfigurationConstants.Keys.ContainerHostPort), defaults.ContainerHostPort);
        var persistDiscoveryCache = Configuration.ReadWithSource(cfg, ConfigurationConstants.FullKey(ConfigurationConstants.Keys.PersistDiscoveryCache), defaults.PersistDiscoveryCache);
        var discoveryCachePath = Configuration.ReadWithSource(cfg, ConfigurationConstants.FullKey(ConfigurationConstants.Keys.DiscoveryCachePath), defaults.DiscoveryCachePath);
        var persistedCacheTtlHours = Configuration.ReadWithSource(cfg, ConfigurationConstants.FullKey(ConfigurationConstants.Keys.PersistedCacheTtlHours), defaults.PersistedCacheTtlHours);
        var preferredStoneName = Configuration.ReadWithSource(cfg, ConfigurationConstants.FullKey(ConfigurationConstants.Keys.PreferredStoneName), defaults.PreferredStoneName);

        module.AddSetting(ConfigurationConstants.Keys.Endpoint, endpoint.Value, source: endpoint.Source, sourceKey: endpoint.ResolvedKey);
        module.AddSetting(ConfigurationConstants.Keys.EnableDiscovery, enableDiscovery.Value.ToString(), source: enableDiscovery.Source, sourceKey: enableDiscovery.ResolvedKey);
        module.AddSetting(ConfigurationConstants.Keys.DiscoveryTimeoutSeconds, discoveryTimeoutSeconds.Value.ToString(), source: discoveryTimeoutSeconds.Source, sourceKey: discoveryTimeoutSeconds.ResolvedKey);
        module.AddSetting(ConfigurationConstants.Keys.DiscoveryPort, discoveryPort.Value.ToString(), source: discoveryPort.Source, sourceKey: discoveryPort.ResolvedKey);
        module.AddSetting(ConfigurationConstants.Keys.DiscoveryMulticastGroup, discoveryGroup.Value, source: discoveryGroup.Source, sourceKey: discoveryGroup.ResolvedKey);
        module.AddSetting(ConfigurationConstants.Keys.DiscoveryCacheTtlSeconds, discoveryCacheTtl.Value.ToString(), source: discoveryCacheTtl.Source, sourceKey: discoveryCacheTtl.ResolvedKey);
        module.AddSetting(ConfigurationConstants.Keys.DiscoveryEnableBroadcastFallback, discoveryBroadcastFallback.Value.ToString(), source: discoveryBroadcastFallback.Source, sourceKey: discoveryBroadcastFallback.ResolvedKey);
        module.AddSetting(ConfigurationConstants.Keys.DiscoveryEnableLimitedBroadcast, discoveryLimitedBroadcast.Value.ToString(), source: discoveryLimitedBroadcast.Source, sourceKey: discoveryLimitedBroadcast.ResolvedKey);
        module.AddSetting(ConfigurationConstants.Keys.HttpTimeoutSeconds, timeoutSeconds.Value.ToString(), source: timeoutSeconds.Source, sourceKey: timeoutSeconds.ResolvedKey);
        module.AddSetting(ConfigurationConstants.Keys.StreamReconnectDelaySeconds, reconnectSeconds.Value.ToString(), source: reconnectSeconds.Source, sourceKey: reconnectSeconds.ResolvedKey);
        module.AddSetting(ConfigurationConstants.Keys.DedupeWindowSize, dedupeWindow.Value.ToString(), source: dedupeWindow.Source, sourceKey: dedupeWindow.ResolvedKey);
        module.AddSetting(ConfigurationConstants.Keys.RequireHostMossWhenContainerized, requireHostMoss.Value.ToString(), source: requireHostMoss.Source, sourceKey: requireHostMoss.ResolvedKey);
        module.AddSetting(ConfigurationConstants.Keys.ContainerHost, containerHost.Value, source: containerHost.Source, sourceKey: containerHost.ResolvedKey);
        module.AddSetting(ConfigurationConstants.Keys.ContainerHostPort, containerHostPort.Value.ToString(), source: containerHostPort.Source, sourceKey: containerHostPort.ResolvedKey);
        module.AddSetting(ConfigurationConstants.Keys.PersistDiscoveryCache, persistDiscoveryCache.Value.ToString(), source: persistDiscoveryCache.Source, sourceKey: persistDiscoveryCache.ResolvedKey);
        module.AddSetting(ConfigurationConstants.Keys.DiscoveryCachePath, discoveryCachePath.Value is null ? "(automatic)" : "(configured)", source: discoveryCachePath.Source, sourceKey: discoveryCachePath.ResolvedKey);
        module.AddSetting(ConfigurationConstants.Keys.PersistedCacheTtlHours, persistedCacheTtlHours.Value.ToString(), source: persistedCacheTtlHours.Source, sourceKey: persistedCacheTtlHours.ResolvedKey);
        module.AddSetting(ConfigurationConstants.Keys.PreferredStoneName, preferredStoneName.Value ?? "(none)", source: preferredStoneName.Source, sourceKey: preferredStoneName.ResolvedKey);

        // Koi topology handler
        var koiEnabled = Configuration.ReadWithSource(cfg, ConfigurationConstants.FullKey(ConfigurationConstants.Keys.KoiDiscoveryEnabled), defaults.KoiDiscoveryEnabled);
        var koiEndpoint = Configuration.ReadWithSource(cfg, ConfigurationConstants.FullKey(ConfigurationConstants.Keys.KoiEndpoint), defaults.KoiEndpoint);
        var koiHealthTimeout = Configuration.ReadWithSource(cfg, ConfigurationConstants.FullKey(ConfigurationConstants.Keys.KoiHealthTimeout), defaults.KoiHealthTimeout);
        var koiContinuous = Configuration.ReadWithSource(cfg, ConfigurationConstants.FullKey(ConfigurationConstants.Keys.KoiContinuousDiscovery), defaults.KoiContinuousDiscovery);
        var koiLantern = Configuration.ReadWithSource(cfg, ConfigurationConstants.FullKey(ConfigurationConstants.Keys.KoiLanternDiscovery), defaults.KoiLanternDiscovery);
        var koiRetryInterval = Configuration.ReadWithSource(cfg, ConfigurationConstants.FullKey(ConfigurationConstants.Keys.KoiRetryInterval), defaults.KoiRetryInterval);

        module.AddSetting(ConfigurationConstants.Keys.KoiDiscoveryEnabled, koiEnabled.Value.ToString(), source: koiEnabled.Source, sourceKey: koiEnabled.ResolvedKey);
        module.AddSetting(ConfigurationConstants.Keys.KoiEndpoint, koiEndpoint.Value ?? "(auto-detected)", source: koiEndpoint.Source, sourceKey: koiEndpoint.ResolvedKey);
        module.AddSetting(ConfigurationConstants.Keys.KoiHealthTimeout, koiHealthTimeout.Value.ToString(), source: koiHealthTimeout.Source, sourceKey: koiHealthTimeout.ResolvedKey);
        module.AddSetting(ConfigurationConstants.Keys.KoiContinuousDiscovery, koiContinuous.Value.ToString(), source: koiContinuous.Source, sourceKey: koiContinuous.ResolvedKey);
        module.AddSetting(ConfigurationConstants.Keys.KoiLanternDiscovery, koiLantern.Value.ToString(), source: koiLantern.Source, sourceKey: koiLantern.ResolvedKey);
        module.AddSetting(ConfigurationConstants.Keys.KoiRetryInterval, koiRetryInterval.Value.ToString(), source: koiRetryInterval.Source, sourceKey: koiRetryInterval.ResolvedKey);

        if (koiEnabled.Value)
        {
            module.AddTool(
                "Koi Topology Handler",
                koiEndpoint.Value ?? $"http://localhost:{Constants.Koi.DefaultPort}",
                "Background mDNS-to-HTTP topology handler via Koi daemon.",
                capability: "zen-garden.koi");
        }

        module.AddTool(
            "Zen Garden Topology",
            Constants.Moss.TopologyEndpoint,
            "Remote garden topology endpoint for active Stone roster hydration.",
            capability: "zen-garden.topology");

        module.AddTool(
            "Zen Garden Tools Snapshot",
            Constants.Moss.ToolsEndpoint,
            "Remote tools-domain snapshot endpoint consumed by Koan.ZenGarden.",
            capability: "zen-garden.tools.snapshot");

        module.AddTool(
            "Zen Garden Tools Stream",
            Constants.Moss.ToolsStreamEndpoint,
            "Remote tools-domain event stream consumed by Koan.ZenGarden.",
            capability: "zen-garden.tools.stream");
    }
}
