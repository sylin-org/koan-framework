using System;
using System.Collections.Generic;
using System.Linq;
using Koan.AI.Contracts.Options;
using Koan.AI.Infrastructure;
using Koan.AI.Pillars;
using Koan.AI.Providers;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Provenance;
using Koan.Core.Semantics.Contributions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Koan.AI.Initialization;

public sealed class AiModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        AiPillarManifest.EnsureRegistered();

        var planBuilder = new AiProviderPlanBuilder();
        SemanticContributionPlans.Schedule<AiProviderContributionTarget, AiProviderPlan>(
            services,
            planBuilder.ForOwner,
            planBuilder.Build,
            static (collection, plan) =>
            {
                collection.Replace(ServiceDescriptor.Singleton(plan));
                foreach (var provider in plan.Providers)
                {
                    collection.TryAdd(ServiceDescriptor.Singleton(provider.ActivatorType, provider.ActivatorType));
                }
            });

        services.TryAddSingleton(AiProviderPlan.Empty);
        services.AddAi();
        services.TryAddSingleton<AiProviderPlanInitializer>();
        services.TryAddSingleton<AiProvenancePublisher>();
    }

    public override async Task Start(IServiceProvider services, CancellationToken ct)
    {
        await services.GetRequiredService<AiProviderPlanInitializer>()
            .Initialize(ct)
            .ConfigureAwait(false);
        await services.GetRequiredService<AiProvenancePublisher>()
            .Publish(ct)
            .ConfigureAwait(false);
    }

    public override void Report(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);

        var configuration = cfg ?? new ConfigurationBuilder().Build();
        var defaults = new AiOptions();

        var autoDiscoveryOption = Configuration.ReadWithSource(configuration, ConfigurationConstants.FullKey(ConfigurationConstants.Keys.AutoDiscoveryEnabled), defaults.AutoDiscoveryEnabled);
        var allowNonDevOption = Configuration.ReadWithSource(configuration, ConfigurationConstants.FullKey(ConfigurationConstants.Keys.AllowDiscoveryInNonDev), defaults.AllowDiscoveryInNonDev);
        var policyOption = Configuration.ReadWithSource(configuration, ConfigurationConstants.FullKey(ConfigurationConstants.Keys.DefaultPolicy), defaults.DefaultPolicy);

        module.PublishConfigValue(KoanAiProvenanceItems.AutoDiscoveryEnabled, autoDiscoveryOption);
        module.PublishConfigValue(KoanAiProvenanceItems.AllowDiscoveryOutsideDevelopment, allowNonDevOption);
        module.PublishConfigValue(KoanAiProvenanceItems.DefaultRoutingPolicy, policyOption);

        DescribeConfiguredSources(module, configuration);
        DescribeCategoryRouting(module, configuration);
        DescribeActiveRecipe(module, configuration);

        module.AddNote("AI pipeline registered via Microsoft.Extensions.AI (default chat + embeddings bridged to existing adapters).");
    }

    private static void DescribeConfiguredSources(ProvenanceModuleWriter module, IConfiguration configuration)
    {
        var sourcesSection = configuration.GetSection(ConfigurationConstants.Sources.Section);
        var configuredSources = sourcesSection.Exists()
            ? sourcesSection.GetChildren().Select(c => c.Key).Where(name => !string.IsNullOrWhiteSpace(name)).ToArray()
            : [];

        if (configuredSources.Length > 0)
        {
            module.SetSetting(KoanAiProvenanceItems.ConfiguredSources.Key, builder =>
            {
                builder
                    .Label(KoanAiProvenanceItems.ConfiguredSources.Label)
                    .Description(KoanAiProvenanceItems.ConfiguredSources.Description)
                    .Value(string.Join(", ", configuredSources))
                    .Source(ProvenanceSettingSource.AppSettings, ConfigurationConstants.Sources.Section)
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

                var provider = child["Provider"] ?? "";
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

    private static void DescribeCategoryRouting(ProvenanceModuleWriter module, IConfiguration configuration)
    {
        var categories = new[] { "Chat", "Embed", "Ocr" };
        var routingDetails = new List<string>();

        foreach (var category in categories)
        {
            var source = configuration[ConfigurationConstants.Category.Source(category)];
            var model = configuration[ConfigurationConstants.Category.Model(category)];
            var via = configuration[ConfigurationConstants.Category.Via(category)];

            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(source)) parts.Add($"source={source}");
            if (!string.IsNullOrWhiteSpace(model)) parts.Add($"model={model}");
            if (!string.IsNullOrWhiteSpace(via)) parts.Add($"via={via}");

            if (parts.Count > 0)
            {
                routingDetails.Add($"{category}: {string.Join(", ", parts)}");
            }
        }

        if (routingDetails.Count > 0)
        {
            module.SetSetting(KoanAiProvenanceItems.CategoryRouting.Key, builder =>
            {
                builder
                    .Label(KoanAiProvenanceItems.CategoryRouting.Label)
                    .Description(KoanAiProvenanceItems.CategoryRouting.Description)
                    .Value(string.Join("; ", routingDetails))
                    .Source(ProvenanceSettingSource.AppSettings, "Koan:Ai:{Category}")
                    .State(ProvenanceSettingState.Configured);
            });
        }
    }

    private static void DescribeActiveRecipe(ProvenanceModuleWriter module, IConfiguration configuration)
    {
        var recipeName = configuration[ConfigurationConstants.Recipe.ActiveRecipe];
        if (string.IsNullOrWhiteSpace(recipeName))
            return;

        var section = configuration.GetSection(ConfigurationConstants.Recipe.ForRecipe(recipeName));
        var bindings = section.Exists()
            ? section.GetChildren()
                .Where(c => !string.IsNullOrWhiteSpace(c.Value))
                .Select(c => $"{c.Key}={c.Value}")
                .ToArray()
            : [];

        module.SetSetting(KoanAiProvenanceItems.ActiveRecipe.Key, builder =>
        {
            builder
                .Label(KoanAiProvenanceItems.ActiveRecipe.Label)
                .Description(KoanAiProvenanceItems.ActiveRecipe.Description)
                .Value(bindings.Length > 0
                    ? $"{recipeName} ({string.Join(", ", bindings)})"
                    : $"{recipeName} (no bindings found)")
                .Source(ProvenanceSettingSource.AppSettings, "Koan:Ai:ActiveRecipe")
                .State(bindings.Length > 0
                    ? ProvenanceSettingState.Configured
                    : ProvenanceSettingState.Unknown);
        });
    }

}

