using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Provenance;
using static Koan.Core.Hosting.Bootstrap.BootSettingSource;
using Koan.ZenGarden.Extensions;
using Koan.ZenGarden.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Koan.ZenGarden.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.ZenGarden";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddKoanZenGarden();
    }

    public void Describe(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);

        var defaults = new ZenGardenOptions();
        var endpoint = Configuration.ReadWithSource(cfg, $"{ZenGardenOptions.SectionName}:Endpoint", defaults.Endpoint);
        var enableDiscovery = Configuration.ReadWithSource(cfg, $"{ZenGardenOptions.SectionName}:EnableDiscovery", defaults.EnableDiscovery);
        var discoveryTimeoutSeconds = Configuration.ReadWithSource(cfg, $"{ZenGardenOptions.SectionName}:DiscoveryTimeoutSeconds", defaults.DiscoveryTimeoutSeconds);
        var discoveryPort = Configuration.ReadWithSource(cfg, $"{ZenGardenOptions.SectionName}:DiscoveryPort", defaults.DiscoveryPort);
        var discoveryGroup = Configuration.ReadWithSource(cfg, $"{ZenGardenOptions.SectionName}:DiscoveryMulticastGroup", defaults.DiscoveryMulticastGroup);
        var discoveryCacheTtl = Configuration.ReadWithSource(cfg, $"{ZenGardenOptions.SectionName}:DiscoveryCacheTtlSeconds", defaults.DiscoveryCacheTtlSeconds);
        var discoveryBroadcastFallback = Configuration.ReadWithSource(cfg, $"{ZenGardenOptions.SectionName}:DiscoveryEnableBroadcastFallback", defaults.DiscoveryEnableBroadcastFallback);
        var discoveryLimitedBroadcast = Configuration.ReadWithSource(cfg, $"{ZenGardenOptions.SectionName}:DiscoveryEnableLimitedBroadcast", defaults.DiscoveryEnableLimitedBroadcast);
        var timeoutSeconds = Configuration.ReadWithSource(cfg, $"{ZenGardenOptions.SectionName}:HttpTimeoutSeconds", defaults.HttpTimeoutSeconds);
        var reconnectSeconds = Configuration.ReadWithSource(cfg, $"{ZenGardenOptions.SectionName}:StreamReconnectDelaySeconds", defaults.StreamReconnectDelaySeconds);
        var dedupeWindow = Configuration.ReadWithSource(cfg, $"{ZenGardenOptions.SectionName}:DedupeWindowSize", defaults.DedupeWindowSize);
        var requireHostMoss = Configuration.ReadWithSource(cfg, $"{ZenGardenOptions.SectionName}:RequireHostMossWhenContainerized", defaults.RequireHostMossWhenContainerized);
        var containerHost = Configuration.ReadWithSource(cfg, $"{ZenGardenOptions.SectionName}:ContainerHost", defaults.ContainerHost);
        var containerHostPort = Configuration.ReadWithSource(cfg, $"{ZenGardenOptions.SectionName}:ContainerHostPort", defaults.ContainerHostPort);
        var persistDiscoveryCache = Configuration.ReadWithSource(cfg, $"{ZenGardenOptions.SectionName}:PersistDiscoveryCache", defaults.PersistDiscoveryCache);
        var discoveryCachePath = Configuration.ReadWithSource(cfg, $"{ZenGardenOptions.SectionName}:DiscoveryCachePath", defaults.DiscoveryCachePath);
        var persistedCacheTtlHours = Configuration.ReadWithSource(cfg, $"{ZenGardenOptions.SectionName}:PersistedCacheTtlHours", defaults.PersistedCacheTtlHours);
        var preferredStoneName = Configuration.ReadWithSource(cfg, $"{ZenGardenOptions.SectionName}:PreferredStoneName", defaults.PreferredStoneName);

        module.AddSetting("Endpoint", endpoint.Value, source: endpoint.Source, sourceKey: endpoint.ResolvedKey);
        module.AddSetting("EnableDiscovery", enableDiscovery.Value.ToString(), source: enableDiscovery.Source, sourceKey: enableDiscovery.ResolvedKey);
        module.AddSetting("DiscoveryTimeoutSeconds", discoveryTimeoutSeconds.Value.ToString(), source: discoveryTimeoutSeconds.Source, sourceKey: discoveryTimeoutSeconds.ResolvedKey);
        module.AddSetting("DiscoveryPort", discoveryPort.Value.ToString(), source: discoveryPort.Source, sourceKey: discoveryPort.ResolvedKey);
        module.AddSetting("DiscoveryMulticastGroup", discoveryGroup.Value, source: discoveryGroup.Source, sourceKey: discoveryGroup.ResolvedKey);
        module.AddSetting("DiscoveryCacheTtlSeconds", discoveryCacheTtl.Value.ToString(), source: discoveryCacheTtl.Source, sourceKey: discoveryCacheTtl.ResolvedKey);
        module.AddSetting("DiscoveryEnableBroadcastFallback", discoveryBroadcastFallback.Value.ToString(), source: discoveryBroadcastFallback.Source, sourceKey: discoveryBroadcastFallback.ResolvedKey);
        module.AddSetting("DiscoveryEnableLimitedBroadcast", discoveryLimitedBroadcast.Value.ToString(), source: discoveryLimitedBroadcast.Source, sourceKey: discoveryLimitedBroadcast.ResolvedKey);
        module.AddSetting("HttpTimeoutSeconds", timeoutSeconds.Value.ToString(), source: timeoutSeconds.Source, sourceKey: timeoutSeconds.ResolvedKey);
        module.AddSetting("StreamReconnectDelaySeconds", reconnectSeconds.Value.ToString(), source: reconnectSeconds.Source, sourceKey: reconnectSeconds.ResolvedKey);
        module.AddSetting("DedupeWindowSize", dedupeWindow.Value.ToString(), source: dedupeWindow.Source, sourceKey: dedupeWindow.ResolvedKey);
        module.AddSetting("RequireHostMossWhenContainerized", requireHostMoss.Value.ToString(), source: requireHostMoss.Source, sourceKey: requireHostMoss.ResolvedKey);
        module.AddSetting("ContainerHost", containerHost.Value, source: containerHost.Source, sourceKey: containerHost.ResolvedKey);
        module.AddSetting("ContainerHostPort", containerHostPort.Value.ToString(), source: containerHostPort.Source, sourceKey: containerHostPort.ResolvedKey);
        module.AddSetting("PersistDiscoveryCache", persistDiscoveryCache.Value.ToString(), source: persistDiscoveryCache.Source, sourceKey: persistDiscoveryCache.ResolvedKey);
        module.AddSetting("DiscoveryCachePath", discoveryCachePath.Value ?? "(auto-resolved)", source: discoveryCachePath.Source, sourceKey: discoveryCachePath.ResolvedKey);
        module.AddSetting("PersistedCacheTtlHours", persistedCacheTtlHours.Value.ToString(), source: persistedCacheTtlHours.Source, sourceKey: persistedCacheTtlHours.ResolvedKey);
        module.AddSetting("PreferredStoneName", preferredStoneName.Value ?? "(none)", source: preferredStoneName.Source, sourceKey: preferredStoneName.ResolvedKey);

        // Koi topology handler
        var koiEnabled = Configuration.ReadWithSource(cfg, $"{ZenGardenOptions.SectionName}:KoiDiscoveryEnabled", defaults.KoiDiscoveryEnabled);
        var koiEndpoint = Configuration.ReadWithSource(cfg, $"{ZenGardenOptions.SectionName}:KoiEndpoint", defaults.KoiEndpoint);
        var koiHealthTimeout = Configuration.ReadWithSource(cfg, $"{ZenGardenOptions.SectionName}:KoiHealthTimeout", defaults.KoiHealthTimeout);
        var koiContinuous = Configuration.ReadWithSource(cfg, $"{ZenGardenOptions.SectionName}:KoiContinuousDiscovery", defaults.KoiContinuousDiscovery);
        var koiLantern = Configuration.ReadWithSource(cfg, $"{ZenGardenOptions.SectionName}:KoiLanternDiscovery", defaults.KoiLanternDiscovery);
        var koiRetryInterval = Configuration.ReadWithSource(cfg, $"{ZenGardenOptions.SectionName}:KoiRetryInterval", defaults.KoiRetryInterval);

        module.AddSetting("KoiDiscoveryEnabled", koiEnabled.Value.ToString(), source: koiEnabled.Source, sourceKey: koiEnabled.ResolvedKey);
        module.AddSetting("KoiEndpoint", koiEndpoint.Value ?? "(auto-detected)", source: koiEndpoint.Source, sourceKey: koiEndpoint.ResolvedKey);
        module.AddSetting("KoiHealthTimeout", koiHealthTimeout.Value.ToString(), source: koiHealthTimeout.Source, sourceKey: koiHealthTimeout.ResolvedKey);
        module.AddSetting("KoiContinuousDiscovery", koiContinuous.Value.ToString(), source: koiContinuous.Source, sourceKey: koiContinuous.ResolvedKey);
        module.AddSetting("KoiLanternDiscovery", koiLantern.Value.ToString(), source: koiLantern.Source, sourceKey: koiLantern.ResolvedKey);
        module.AddSetting("KoiRetryInterval", koiRetryInterval.Value.ToString(), source: koiRetryInterval.Source, sourceKey: koiRetryInterval.ResolvedKey);

        if (persistDiscoveryCache.Value)
        {
            var resolvedPath = StoneRosterPathResolver.Resolve(
                new ZenGardenOptions { DiscoveryCachePath = discoveryCachePath.Value });
            module.AddSetting("DiscoveryCachePath (resolved)", resolvedPath, source: Custom);
        }

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
