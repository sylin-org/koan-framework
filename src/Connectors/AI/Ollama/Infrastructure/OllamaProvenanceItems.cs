using System.Collections.Generic;
using System.Globalization;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Provenance;
using Koan.AI.Connector.Ollama.Options;

namespace Koan.AI.Connector.Ollama.Infrastructure;

internal static class OllamaProvenanceItems
{
    private const string ConnectionKey = Constants.Configuration.Keys.ConnectionString;
    private const string BaseUrlKey = Constants.Section + ":BaseUrl";
    private const string DefaultModelKey = Constants.Section + ":DefaultModel";
    private const string AutoDownloadModelsKey = Constants.Section + ":AutoDownloadModels";
    private const string DefaultPageSizeKey = Constants.Section + ":DefaultPageSize";
    private const string MaxPageSizeKey = Constants.Section + ":MaxPageSize";

    private static readonly OllamaOptions Defaults = new();

    private static readonly IReadOnlyCollection<string> ConnectionConsumers = new[]
    {
        "Koan.AI.Connector.Ollama.Options.OllamaOptionsConfigurator",
        "Koan.AI.Connector.Ollama.OllamaAdapter",
        "Koan.AI.Connector.Ollama.Initialization.KoanAutoRegistrar"
    };

    private static readonly IReadOnlyCollection<string> BaseUrlConsumers = new[]
    {
        "Koan.AI.Connector.Ollama.Options.OllamaOptionsConfigurator",
        "Koan.AI.Connector.Ollama.OllamaAdapter"
    };

    private static readonly IReadOnlyCollection<string> ModelConsumers = new[]
    {
        "Koan.AI.Connector.Ollama.Options.OllamaOptionsConfigurator",
        "Koan.AI.Connector.Ollama.OllamaAdapter",
        "Koan.AI.Connector.Ollama.Infrastructure.OllamaModelManager"
    };

    private static readonly IReadOnlyCollection<string> PagingConsumers = new[]
    {
        "Koan.AI.Connector.Ollama.OllamaAdapter"
    };

    internal static readonly ProvenanceItem ConnectionString = new(
        ConnectionKey,
        "Ollama Connection",
        "Connection string or base URL used to reach the Ollama service.",
        MustSanitize: true,
        DefaultValue: Defaults.ConnectionString,
        DefaultConsumers: ConnectionConsumers);

    internal static readonly ProvenanceItem BaseUrl = new(
        BaseUrlKey,
        "Ollama Base URL",
        "HTTP base address applied to Ollama API requests.",
        DefaultValue: Defaults.BaseUrl,
        DefaultConsumers: BaseUrlConsumers);

    internal static readonly ProvenanceItem DefaultModel = new(
        DefaultModelKey,
        "Default Model",
        "Model identifier used when callers do not request a specific Ollama model.",
        DefaultValue: Defaults.DefaultModel ?? "none",
        DefaultConsumers: ModelConsumers);

    internal static readonly ProvenanceItem AutoDownloadModels = new(
        AutoDownloadModelsKey,
        "Auto Download Models",
        "Indicates whether missing Ollama models are automatically downloaded.",
        DefaultValue: BoolString(Defaults.AutoDownloadModels),
        DefaultConsumers: ModelConsumers);

    internal static readonly ProvenanceItem DefaultPageSize = new(
        DefaultPageSizeKey,
        "Default Page Size",
        "Default batch size for AI vector or chat pagination operations.",
        DefaultValue: Defaults.DefaultPageSize.ToString(CultureInfo.InvariantCulture),
        DefaultConsumers: PagingConsumers);

    internal static readonly ProvenanceItem MaxPageSize = new(
        MaxPageSizeKey,
        "Max Page Size",
        "Maximum batch size permitted for AI pagination operations.",
        DefaultValue: Defaults.MaxPageSize.ToString(CultureInfo.InvariantCulture),
        DefaultConsumers: PagingConsumers);

    private static string BoolString(bool value) => value ? "true" : "false";
}
