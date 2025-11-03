using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Koan.AI.Connector.Ollama.Health;
using Koan.AI.Connector.Ollama.Orchestration;
using Koan.AI.Connector.Ollama.Discovery;
using Koan.AI.Connector.Ollama.Options;
using Koan.Core;
using Koan.Core.Adapters.Reporting;
using Koan.Core.Modules;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using Koan.Core.Provenance;
using Koan.Core.Hosting.Bootstrap;
using OllamaItems = Koan.AI.Connector.Ollama.Infrastructure.OllamaProvenanceItems;
using ProvenanceModes = Koan.Core.Hosting.Bootstrap.ProvenancePublicationModeExtensions;

namespace Koan.AI.Connector.Ollama.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.AI.Connector.Ollama";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        // Use consistent autonomous discovery pattern like all other service adapters
        services.AddKoanOptions<OllamaOptions>(Infrastructure.Constants.Section);
        services.AddSingleton<IConfigureOptions<OllamaOptions>, OllamaOptionsConfigurator>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IServiceDiscoveryAdapter, OllamaDiscoveryAdapter>());

        // Register the hosted service that creates and registers OllamaAdapter instances to the AI registry
        services.AddHostedService<OllamaDiscoveryService>();

        // Register orchestration evaluator for dependency management
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IKoanOrchestrationEvaluator, OllamaOrchestrationEvaluator>());

        // Health reporter so readiness can reflect Ollama availability and models
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, OllamaHealthContributor>());
    }

    public void Describe(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);
        module.AddNote("Ollama discovery handled by autonomous OllamaDiscoveryAdapter");

        var defaultOptions = new OllamaOptions();

        var connection = Configuration.ReadFirstWithSource(
            cfg,
            defaultOptions.ConnectionString,
            Infrastructure.Constants.Configuration.Keys.ConnectionString,
            Infrastructure.Constants.Configuration.Keys.AltConnectionString,
            "ConnectionStrings:Ollama",
            "ConnectionStrings:Default");

        var baseUrl = Configuration.ReadFirstWithSource(
            cfg,
            defaultOptions.BaseUrl,
            Infrastructure.Constants.Section + ":BaseUrl",
            "Koan:Ai:Ollama:BaseUrl");

        var defaultModel = Configuration.ReadFirstWithSource(
            cfg,
            defaultOptions.DefaultModel ?? "none",
            Infrastructure.Constants.Section + ":DefaultModel",
            "Koan:Ai:Ollama:DefaultModel");

        var autoDownload = Configuration.ReadFirstWithSource(
            cfg,
            defaultOptions.AutoDownloadModels,
            Infrastructure.Constants.Section + ":AutoDownloadModels",
            "Koan:Ai:Ollama:AutoDownloadModels");

        var defaultPageSize = Configuration.ReadFirstWithSource(
            cfg,
            defaultOptions.DefaultPageSize,
            Infrastructure.Constants.Section + ":DefaultPageSize");

        var maxPageSize = Configuration.ReadFirstWithSource(
            cfg,
            defaultOptions.MaxPageSize,
            Infrastructure.Constants.Section + ":MaxPageSize");

        var connectionIsAuto = string.IsNullOrWhiteSpace(connection.Value) || string.Equals(connection.Value, "auto", StringComparison.OrdinalIgnoreCase);
        var connectionSourceKey = connection.ResolvedKey ?? Infrastructure.Constants.Configuration.Keys.ConnectionString;

        var discoveryParameters = BuildDiscoveryParameters(defaultModel.Value, autoDownload.Value);
        var effectiveConnection = connection.Value ?? defaultOptions.ConnectionString;
        if (connectionIsAuto)
        {
            var adapter = new OllamaDiscoveryAdapter(cfg, NullLogger<OllamaDiscoveryAdapter>.Instance);
            effectiveConnection = AdapterBootReporting.ResolveConnectionString(
                cfg,
                adapter,
                discoveryParameters,
                () => BuildOllamaFallback(defaultOptions));
        }

        var connectionMode = connectionIsAuto
            ? ProvenanceModes.FromBootSource(BootSettingSource.Auto, usedDefault: true)
            : ProvenanceModes.FromConfigurationValue(connection);

        module.PublishConfigValue(
            OllamaItems.ConnectionString,
            connection,
            displayOverride: effectiveConnection,
            modeOverride: connectionMode,
            usedDefaultOverride: connectionIsAuto ? true : connection.UsedDefault,
            sourceKeyOverride: connectionSourceKey);

        var baseUrlDisplay = baseUrl.Value;
        if (connectionIsAuto && (baseUrl.UsedDefault || string.Equals(baseUrl.Value, defaultOptions.BaseUrl, StringComparison.OrdinalIgnoreCase)))
        {
            baseUrlDisplay = effectiveConnection;
        }

        module.PublishConfigValue(OllamaItems.BaseUrl, baseUrl, displayOverride: baseUrlDisplay);
        module.PublishConfigValue(OllamaItems.DefaultModel, defaultModel);
        module.PublishConfigValue(OllamaItems.AutoDownloadModels, autoDownload, sanitizeOverride: false);
        module.PublishConfigValue(OllamaItems.DefaultPageSize, defaultPageSize);
        module.PublishConfigValue(OllamaItems.MaxPageSize, maxPageSize);
    }

    private static Dictionary<string, object>? BuildDiscoveryParameters(string? defaultModel, bool autoDownload)
    {
        var parameters = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(defaultModel) && !string.Equals(defaultModel, "none", StringComparison.OrdinalIgnoreCase))
        {
            parameters["requiredModel"] = defaultModel;
        }

        parameters["autoDownloadModels"] = autoDownload;

        return parameters.Count > 0 ? parameters : null;
    }

    private static string BuildOllamaFallback(OllamaOptions defaults)
    {
        if (!string.IsNullOrWhiteSpace(defaults.BaseUrl))
        {
            return defaults.BaseUrl;
        }

        var port = Infrastructure.Constants.Discovery.DefaultPort;
        return $"http://localhost:{port}";
    }
}


