using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Provenance;
using Koan.ZenGarden.Extensions;
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
