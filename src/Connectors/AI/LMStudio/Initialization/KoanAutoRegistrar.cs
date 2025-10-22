using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Koan.AI.Connector.LMStudio.Discovery;
using Koan.AI.Connector.LMStudio.Infrastructure;
using Koan.AI.Connector.LMStudio.Options;
using Koan.AI.Connector.LMStudio.Orchestration;
using Koan.Core;
using Koan.Core.Adapters.Reporting;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using Koan.Core.Provenance;
using ProvenanceModes = Koan.Core.Hosting.Bootstrap.ProvenancePublicationModeExtensions;
using KoanConfiguration = Koan.Core.Configuration;

namespace Koan.AI.Connector.LMStudio.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.AI.Connector.LMStudio";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddKoanOptions<LMStudioOptions>(Constants.Section);
        services.AddSingleton<IConfigureOptions<LMStudioOptions>, LMStudioOptionsConfigurator>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IServiceDiscoveryAdapter, LMStudioDiscoveryAdapter>());

        services.AddHostedService<LMStudioDiscoveryService>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IKoanOrchestrationEvaluator, LMStudioOrchestrationEvaluator>());
    }

    public void Describe(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);
        module.AddNote("LM Studio discovery handled by autonomous LMStudioDiscoveryAdapter");

        var defaults = new LMStudioOptions();

        var connection = KoanConfiguration.ReadFirstWithSource(
                cfg,
                defaults.ConnectionString,
                Constants.Configuration.Keys.ConnectionString,
                Constants.Configuration.Keys.AltConnectionString,
                "ConnectionStrings:LMStudio",
                "ConnectionStrings:Default");

        var baseUrl = KoanConfiguration.ReadFirstWithSource(
                cfg,
                defaults.BaseUrl,
                Constants.Section + ":BaseUrl",
                "Koan:Ai:LMStudio:BaseUrl");

        var defaultModel = KoanConfiguration.ReadFirstWithSource(
                cfg,
                defaults.DefaultModel ?? "none",
                Constants.Section + ":DefaultModel",
                "Koan:Ai:LMStudio:DefaultModel");

        var apiKey = KoanConfiguration.ReadFirstWithSource(
                cfg,
                defaults.ApiKey ?? string.Empty,
                Constants.Configuration.Keys.ApiKey,
                Constants.Discovery.EnvKey);

        Publish(module, LMStudioProvenanceItems.ConnectionString, connection);
        Publish(module, LMStudioProvenanceItems.BaseUrl, baseUrl, displayOverride: ResolveDisplayBase(cfg, connection, baseUrl, defaults));
        Publish(module, LMStudioProvenanceItems.DefaultModel, defaultModel);
        Publish(module, LMStudioProvenanceItems.ApiKey, apiKey, sanitizeOverride: true);
    }

    private static void Publish<T>(
        ProvenanceModuleWriter module,
        ProvenanceItem item,
        ConfigurationValue<T> value,
        object? displayOverride = null,
        ProvenancePublicationMode? modeOverride = null,
        bool? usedDefaultOverride = null,
        string? sourceKeyOverride = null,
        bool? sanitizeOverride = null)
    {
        module.AddSetting(
            item,
            modeOverride ?? ProvenanceModes.FromConfigurationValue(value),
            displayOverride ?? value.Value,
            sourceKey: sourceKeyOverride ?? value.ResolvedKey,
            usedDefault: usedDefaultOverride ?? value.UsedDefault,
            sanitizeOverride: sanitizeOverride);
    }

    private static object ResolveDisplayBase(
        IConfiguration cfg,
        ConfigurationValue<string> connection,
        ConfigurationValue<string> baseUrl,
        LMStudioOptions defaults)
    {
        var needsFallback = string.IsNullOrWhiteSpace(connection.Value) || string.Equals(connection.Value, "auto", StringComparison.OrdinalIgnoreCase);
        if (!needsFallback)
        {
            return baseUrl.Value ?? defaults.BaseUrl;
        }

        var adapter = new LMStudioDiscoveryAdapter(cfg, NullLogger<LMStudioDiscoveryAdapter>.Instance);
        var resolved = AdapterBootReporting.ResolveConnectionString(
            cfg,
            adapter,
            null,
            () => defaults.BaseUrl);

        return resolved;
    }
}

internal static class LMStudioProvenanceItems
{
    public static readonly ProvenanceItem ConnectionString = new(
        "Koan.AI.LMStudio.ConnectionString",
        "Connection string",
        "LM Studio endpoint or discovery directive (auto).",
        DefaultValue: "auto");

    public static readonly ProvenanceItem BaseUrl = new(
        "Koan.AI.LMStudio.BaseUrl",
        "Base URL",
        "LM Studio base URL when discovery resolves to localhost.",
        DefaultValue: "http://localhost:1234");

    public static readonly ProvenanceItem DefaultModel = new(
        "Koan.AI.LMStudio.DefaultModel",
        "Default model",
        "Model alias used when requests omit an explicit model.");

    public static readonly ProvenanceItem ApiKey = new(
        "Koan.AI.LMStudio.ApiKey",
        "API key",
        "LM Studio Bearer token for secured endpoints.",
        IsSecret: true,
        MustSanitize: true,
        DocumentationLink: "https://lmstudio.ai");
}

