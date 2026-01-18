using System;
using System.Collections.Generic;
using System.Linq;
using Koan.AI.Contracts.Options;
using Koan.AI.Infrastructure;
using Koan.AI.Pillars;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Provenance;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Koan.AI.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.AI";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        AiPillarManifest.EnsureRegistered();
        // Bind options if IConfiguration is present later; AddAi also binds when config is provided.
        services.AddAi();
        services.AddHostedService<AiAdapterContributorInitializer>();
        services.AddHostedService<AiProvenancePublisher>();
    }

    public void Describe(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);

        var configuration = cfg ?? new ConfigurationBuilder().Build();
        var defaults = new AiOptions();

        var autoDiscoveryOption = Configuration.ReadWithSource(configuration, "Koan:Ai:AutoDiscoveryEnabled", defaults.AutoDiscoveryEnabled);
        var allowNonDevOption = Configuration.ReadWithSource(configuration, "Koan:Ai:AllowDiscoveryInNonDev", defaults.AllowDiscoveryInNonDev);
        var policyOption = Configuration.ReadWithSource(configuration, "Koan:Ai:DefaultPolicy", defaults.DefaultPolicy);

        module.PublishConfigValue(KoanAiProvenanceItems.AutoDiscoveryEnabled, autoDiscoveryOption);
        module.PublishConfigValue(KoanAiProvenanceItems.AllowDiscoveryOutsideDevelopment, allowNonDevOption);
        module.PublishConfigValue(KoanAiProvenanceItems.DefaultRoutingPolicy, policyOption);

        DescribeConfiguredSources(module, configuration);
        DescribeLegacyOllama(module, configuration);

        module.AddNote("AI pipeline registered via Microsoft.Extensions.AI (default chat + embeddings bridged to existing adapters).");
    }

    private static void DescribeConfiguredSources(ProvenanceModuleWriter module, IConfiguration configuration)
    {
        var sourcesSection = configuration.GetSection("Koan:Ai:Sources");
        var configuredSources = sourcesSection.Exists()
            ? sourcesSection.GetChildren().Select(c => c.Key).Where(name => !string.IsNullOrWhiteSpace(name)).ToArray()
            : Array.Empty<string>();

        if (configuredSources.Length > 0)
        {
            module.SetSetting(KoanAiProvenanceItems.ConfiguredSources.Key, builder =>
            {
                builder
                    .Label(KoanAiProvenanceItems.ConfiguredSources.Label)
                    .Description(KoanAiProvenanceItems.ConfiguredSources.Description)
                    .Value(string.Join(", ", configuredSources))
                    .Source(ProvenanceSettingSource.AppSettings, "Koan:Ai:Sources")
                    .State(ProvenanceSettingState.Configured);
            });

            var details = new List<string>(configuredSources.Length);
            foreach (var child in sourcesSection.GetChildren())
            {
                var name = child.Key;
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var provider = child["Provider"] ?? string.Empty;
                var policy = child["Policy"] ?? "Fallback";
                int members = 0;
                if (!string.IsNullOrWhiteSpace(provider))
                {
                    members = child.GetSection(provider).GetSection("Urls").Get<string[]>()?.Length ?? 0;
                }
                details.Add($"{name} → provider={provider switch { null or "" => "(missing provider)", var p => p }}, policy={policy}, members={members}");
            }

            if (details.Count > 0)
            {
                module.AddNote("Configured AI sources: " + string.Join("; ", details));
            }
        }
        else
        {
            module.SetSetting(KoanAiProvenanceItems.ConfiguredSources.Key, builder =>
            {
                builder
                    .Label(KoanAiProvenanceItems.ConfiguredSources.Label)
                    .Description(KoanAiProvenanceItems.ConfiguredSources.Description)
                    .Value("(auto-discovery)")
                    .Source(ProvenanceSettingSource.Auto)
                    .State(ProvenanceSettingState.Default);
            });
        }
    }

    private static void DescribeLegacyOllama(ProvenanceModuleWriter module, IConfiguration configuration)
    {
        var baseUrl = Configuration.ReadWithSource(configuration, "Koan:Ai:Ollama:BaseUrl", string.Empty);
        var defaultModel = Configuration.ReadWithSource(configuration, "Koan:Ai:Ollama:DefaultModel", string.Empty);

        if (!string.IsNullOrWhiteSpace(baseUrl.Value) || !baseUrl.UsedDefault)
        {
            module.PublishConfigValue(KoanAiProvenanceItems.LegacyOllamaBaseUrl, baseUrl);
        }

        if (!string.IsNullOrWhiteSpace(defaultModel.Value) || !defaultModel.UsedDefault)
        {
            module.PublishConfigValue(KoanAiProvenanceItems.LegacyOllamaDefaultModel, defaultModel);
        }

        var capabilitySection = configuration.GetSection("Koan:Ai:Ollama:Capabilities");
        if (capabilitySection.Exists())
        {
            var capabilitySummaries = capabilitySection.GetChildren()
                .Select(capability =>
                {
                    var model = capability["Model"] ?? "(missing model)";
                    var autoDownload = capability.GetValue("AutoDownload", true) ? "auto-download" : "manual";
                    return $"{capability.Key}: model={model}, {autoDownload}";
                })
                .ToArray();

            if (capabilitySummaries.Length > 0)
            {
                module.AddNote("Legacy Ollama capabilities → " + string.Join("; ", capabilitySummaries));
            }
        }
    }
}

