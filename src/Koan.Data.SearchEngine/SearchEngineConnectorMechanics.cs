using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Adapters;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Infrastructure;
using Koan.Core.Logging;
using Koan.Core.Modules;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using Koan.Core.Provenance;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Adapters.Configuration;
using Koan.Data.Vector;
using Koan.Data.Vector.Abstractions;

namespace Koan.Data.SearchEngine;

/// <summary>
/// Stable provider vocabulary consumed by the shared Elasticsearch/OpenSearch connector mechanics.
/// Applications select a provider; connector assemblies supply this descriptor and their native dialect.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record SearchEngineConnectorDescriptor(
    string ProviderId,
    string ConfigurationName,
    string Section,
    string ServiceName,
    IReadOnlyCollection<string> ProviderAliases,
    IReadOnlyCollection<string> DiscoveryAliases,
    IReadOnlyCollection<string> EnvironmentVariables,
    IReadOnlyCollection<string> AspireServiceNames,
    string CustomScheme,
    string HttpClientName,
    string DefaultEndpoint,
    ActivitySource ActivitySource,
    Func<ISearchEngineDialect> CreateDialect)
{
    internal string Key(string name) => $"{Section}:{name}";
    internal string ConnectionStringKey => $"ConnectionStrings:{ConfigurationName}";
    internal string HealthLogCategory => $"data.{ProviderId}.health";
}

/// <summary>Shared registration and startup projection for Lucene-family vector connectors.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class SearchEngineConnectorExtensions
{
    public static IServiceCollection AddSearchEngineConnector<TOptions, TFactory>(
        this IServiceCollection services,
        SearchEngineConnectorDescriptor descriptor)
        where TOptions : SearchEngineVectorOptions, new()
        where TFactory : class, IVectorAdapterFactory
    {
        services.AddKoanOptions<TOptions>(descriptor.Section);
        services.AddSingleton<IConfigureOptions<TOptions>>(sp =>
            new SearchEngineOptionsConfigurator<TOptions>(
                sp.GetRequiredService<IConfiguration>(),
                sp.GetService<ILogger<SearchEngineOptionsConfigurator<TOptions>>>(),
                sp.GetRequiredService<IOptions<AdaptersReadinessOptions>>(),
                descriptor,
                sp.GetService<IServiceDiscoveryCoordinator>()));
        services.AddSingleton<IHealthContributor>(sp =>
            new SearchEngineHealthContributor<TOptions>(
                sp.GetRequiredService<IHttpClientFactory>(),
                sp.GetRequiredService<IOptions<TOptions>>(),
                sp.GetRequiredService<IVectorAdapterParticipation>(),
                descriptor,
                sp.GetService<ILogger<SearchEngineHealthContributor<TOptions>>>()));
        services.AddSingleton<IVectorAdapterFactory, TFactory>();
        services.AddHttpClient(descriptor.HttpClientName);
        return services;
    }

    public static void ReportSearchEngineConnector<TOptions, TFactory>(
        this ProvenanceModuleWriter module,
        IConfiguration configuration,
        SearchEngineConnectorDescriptor descriptor,
        Func<IConfiguration, IServiceDiscoveryAdapter> discoveryFactory)
        where TOptions : SearchEngineVectorOptions, new()
        where TFactory : class, IVectorAdapterFactory
    {
        var defaults = new TOptions();
        var connection = Configuration.ReadFirstWithSource(
            configuration,
            defaults.ConnectionString,
            descriptor.ConnectionStringKey,
            descriptor.Key("ConnectionString"),
            descriptor.Key("Endpoint"));
        var isAuto = string.IsNullOrWhiteSpace(connection.Value) ||
                     string.Equals(connection.Value, "auto", StringComparison.OrdinalIgnoreCase);
        var source = isAuto ? BootSettingSource.Auto : connection.Source;
        var sourceKey = connection.ResolvedKey ?? descriptor.Key("Endpoint");
        var endpoint = connection.Value ?? defaults.ConnectionString;
        if (isAuto)
        {
            var discovery = discoveryFactory(configuration);
            endpoint = ServiceDiscoveryReporting.ResolveConnectionString(
                configuration,
                discovery,
                null,
                () => descriptor.DefaultEndpoint);
        }

        var indexPrefix = Configuration.ReadWithSource(
            configuration,
            descriptor.Key("IndexPrefix"),
            defaults.IndexPrefix ?? "koan");
        var vectorField = Configuration.ReadWithSource(
            configuration,
            descriptor.Key("VectorField"),
            defaults.VectorField);
        var metadataField = Configuration.ReadWithSource(
            configuration,
            descriptor.Key("MetadataField"),
            defaults.MetadataField);
        var similarity = Configuration.ReadWithSource(
            configuration,
            descriptor.Key("SimilarityMetric"),
            defaults.SimilarityMetric);
        var timeout = Configuration.ReadWithSource(
            configuration,
            descriptor.Key("TimeoutSeconds"),
            defaults.DefaultTimeoutSeconds);
        var dimension = Configuration.ReadWithSource(
            configuration,
            descriptor.Key("Dimension"),
            defaults.Dimension ?? 0);
        var consumer = typeof(TFactory).FullName ?? typeof(TFactory).Name;

        module.AddNote($"{descriptor.ConfigurationName} is available; readiness begins only after Vector selects it.");
        var consumers = new[] { consumer };
        module.AddSetting("Endpoint", Redaction.DeIdentify(endpoint), source: source, consumers: consumers, sourceKey: sourceKey);
        module.AddSetting("IndexPrefix", indexPrefix.Value ?? "koan", source: indexPrefix.Source, consumers: consumers, sourceKey: indexPrefix.ResolvedKey);
        module.AddSetting("VectorField", vectorField.Value, source: vectorField.Source, consumers: consumers, sourceKey: vectorField.ResolvedKey);
        module.AddSetting("MetadataField", metadataField.Value, source: metadataField.Source, consumers: consumers, sourceKey: metadataField.ResolvedKey);
        module.AddSetting("SimilarityMetric", similarity.Value, source: similarity.Source, consumers: consumers, sourceKey: similarity.ResolvedKey);
        module.AddSetting("TimeoutSeconds", timeout.Value.ToString(), source: timeout.Source, consumers: consumers, sourceKey: timeout.ResolvedKey);
        module.AddSetting("Dimension", dimension.Value.ToString(), source: dimension.Source, consumers: consumers, sourceKey: dimension.ResolvedKey);
    }
}

/// <summary>Constructs the shared REST repository from one provider's selected factory.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public abstract class SearchEngineVectorAdapterFactory<TOptions> : IVectorAdapterFactory
    where TOptions : SearchEngineVectorOptions
{
    protected abstract SearchEngineConnectorDescriptor Descriptor { get; }

    public string Provider => Descriptor.ProviderId;
    public IReadOnlyCollection<string> Aliases => Descriptor.ProviderAliases;

    public IVectorSearchRepository<TEntity, TKey> Create<TEntity, TKey>(
        IServiceProvider services,
        string source = "Default")
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var httpFactory = services.GetRequiredService<IHttpClientFactory>();
        var options = services.GetRequiredService<IOptions<TOptions>>().Value;
        var logger = services.GetService<ILoggerFactory>()
            ?.CreateLogger<SearchEngineVectorRepository<TEntity, TKey>>();
        return new SearchEngineVectorRepository<TEntity, TKey>(
            httpFactory.CreateClient(Descriptor.HttpClientName),
            options,
            Descriptor.CreateDialect(),
            Descriptor.ActivitySource,
            logger,
            services,
            this,
            source);
    }

    public StorageNamingCapability GetNamingCapability(IServiceProvider services) => new()
    {
        Style = StorageNamingStyle.EntityType,
        Casing = NameCasing.Lower,
        PartitionSeparator = '-',
        Partition = new PartitionTokenPolicy { GuidFormat = "N", Lowercase = true, AllowedExtraChars = "-._" }
    };
}

internal sealed class SearchEngineOptionsConfigurator<TOptions> : AdapterOptionsConfigurator<TOptions>
    where TOptions : SearchEngineVectorOptions
{
    private readonly SearchEngineConnectorDescriptor _descriptor;
    private readonly IServiceDiscoveryCoordinator? _discovery;

    protected override string ProviderName => _descriptor.ConfigurationName;

    internal SearchEngineOptionsConfigurator(
        IConfiguration configuration,
        ILogger? logger,
        IOptions<AdaptersReadinessOptions> readiness,
        SearchEngineConnectorDescriptor descriptor,
        IServiceDiscoveryCoordinator? discovery)
        : base(configuration, logger, readiness)
    {
        _descriptor = descriptor;
        _discovery = discovery;
    }

    protected override void ConfigureProviderSpecific(TOptions options)
    {
        var explicitEndpoint = ReadProviderConfiguration(
            "",
            _descriptor.ConnectionStringKey,
            _descriptor.Key("ConnectionString"),
            _descriptor.Key("Endpoint"));
        if (!string.IsNullOrWhiteSpace(explicitEndpoint))
        {
            options.ConnectionString = explicitEndpoint;
            options.Endpoint = explicitEndpoint;
            LogConfiguration(LogLevel.Information, "explicit");
        }
        else if (!string.IsNullOrWhiteSpace(options.ConnectionString) &&
                 !string.Equals(options.ConnectionString.Trim(), "auto", StringComparison.OrdinalIgnoreCase))
        {
            options.Endpoint = options.ConnectionString;
            LogConfiguration(LogLevel.Information, "preconfigured");
        }
        else if (!string.IsNullOrWhiteSpace(options.Endpoint) &&
                 !string.Equals(options.Endpoint, _descriptor.DefaultEndpoint, StringComparison.OrdinalIgnoreCase))
        {
            options.ConnectionString = options.Endpoint;
            LogConfiguration(LogLevel.Information, "preconfigured-endpoint");
        }
        else
        {
            options.ConnectionString = ResolveAutonomousConnection();
            options.Endpoint = options.ConnectionString;
            LogConfiguration(LogLevel.Information, "auto");
        }

        options.ApiKey = NullIfBlank(ReadProviderConfiguration(options.ApiKey ?? "", _descriptor.Key("ApiKey")));
        options.Username = NullIfBlank(ReadProviderConfiguration(options.Username ?? "", _descriptor.Key("Username")));
        options.Password = NullIfBlank(ReadProviderConfiguration(options.Password ?? "", _descriptor.Key("Password")));
        options.IndexPrefix = NullIfBlank(ReadProviderConfiguration(options.IndexPrefix ?? "koan", _descriptor.Key("IndexPrefix")));
        options.IndexName = NullIfBlank(ReadProviderConfiguration(options.IndexName ?? "", _descriptor.Key("IndexName")));
        options.VectorField = ReadProviderConfiguration(options.VectorField, _descriptor.Key("VectorField"));
        options.MetadataField = ReadProviderConfiguration(options.MetadataField, _descriptor.Key("MetadataField"));
        options.IdField = ReadProviderConfiguration(options.IdField, _descriptor.Key("IdField"));
        options.SimilarityMetric = ReadProviderConfiguration(options.SimilarityMetric, _descriptor.Key("SimilarityMetric"));
        options.RefreshMode = ReadProviderConfiguration(options.RefreshMode, _descriptor.Key("RefreshMode"));
        options.DefaultTimeoutSeconds = ReadProviderConfiguration(options.DefaultTimeoutSeconds, _descriptor.Key("TimeoutSeconds"));
        options.DisableIndexAutoCreate = ReadProviderConfiguration(options.DisableIndexAutoCreate, _descriptor.Key("DisableIndexAutoCreate"));
        if (int.TryParse(Configuration[_descriptor.Key("Dimension")], out var dimension))
            options.Dimension = dimension;

        LogConfiguration(
            LogLevel.Information,
            "final",
            ("endpoint", options.Endpoint),
            ("indexPrefix", options.IndexPrefix),
            ("dimension", options.Dimension));
    }

    private string ResolveAutonomousConnection()
    {
        if (Koan.Core.Configuration.Read(Configuration, _descriptor.Key("DisableAutoDetection"), false))
        {
            LogDiscovery(LogLevel.Information, "disabled", ("fallback", _descriptor.DefaultEndpoint));
            return _descriptor.DefaultEndpoint;
        }

        if (_discovery is null)
        {
            LogDiscovery(LogLevel.Warning, "coordinator-missing", ("fallback", _descriptor.DefaultEndpoint));
            return _descriptor.DefaultEndpoint;
        }

        try
        {
            var result = _discovery.DiscoverService(
                    _descriptor.ServiceName,
                    new DiscoveryContext
                    {
                        OrchestrationMode = KoanEnv.OrchestrationMode,
                        HealthCheckTimeout = TimeSpan.FromMilliseconds(500),
                        Parameters = new Dictionary<string, object>()
                    })
                .GetAwaiter()
                .GetResult();
            if (result.IsSuccessful)
            {
                LogDiscovery(LogLevel.Information, "success", ("url", result.ServiceUrl));
                return result.ServiceUrl;
            }

            LogDiscovery(LogLevel.Warning, "fallback", ("reason", result.ErrorMessage), ("fallback", _descriptor.DefaultEndpoint));
            return _descriptor.DefaultEndpoint;
        }
        catch (Exception ex)
        {
            LogDiscovery(LogLevel.Error, "exception", ("error", ex), ("fallback", _descriptor.DefaultEndpoint));
            return _descriptor.DefaultEndpoint;
        }
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}

/// <summary>Shared autonomous discovery mechanics for Lucene-family providers.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public abstract class SearchEngineDiscoveryAdapter : ServiceDiscoveryAdapterBase
{
    private readonly SearchEngineConnectorDescriptor _descriptor;

    protected SearchEngineDiscoveryAdapter(
        IConfiguration configuration,
        ILogger logger,
        SearchEngineConnectorDescriptor descriptor)
        : base(configuration, logger)
    {
        _descriptor = descriptor;
    }

    protected abstract Type FactoryType { get; }
    public override string ServiceName => _descriptor.ServiceName;
    public override string[] Aliases => _descriptor.DiscoveryAliases.ToArray();
    protected override Type GetFactoryType() => FactoryType;

    protected override async Task<bool> ValidateServiceHealth(
        string serviceUrl,
        DiscoveryContext context,
        CancellationToken cancellationToken)
    {
        using var http = new HttpClient { Timeout = context.HealthCheckTimeout };
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(context.HealthCheckTimeout);
        var response = await http.GetAsync(new Uri(new Uri(serviceUrl), "/_cluster/health"), cts.Token).ConfigureAwait(false);
        if (response.IsSuccessStatusCode) return true;
        return (await http.GetAsync(serviceUrl, cts.Token).ConfigureAwait(false)).IsSuccessStatusCode;
    }

    protected override string? ReadExplicitConfiguration() =>
        _configuration.GetConnectionString(_descriptor.ConfigurationName) ??
        _configuration[_descriptor.Key("ConnectionString")] ??
        _configuration[_descriptor.Key("Endpoint")];

    protected override IEnumerable<DiscoveryCandidate> GetEnvironmentCandidates()
    {
        foreach (var variable in _descriptor.EnvironmentVariables)
        {
            var value = Environment.GetEnvironmentVariable(variable);
            if (string.IsNullOrWhiteSpace(value)) continue;
            return value.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(url => new DiscoveryCandidate(
                    url,
                    $"environment-{variable.ToLowerInvariant().Replace('_', '-')}",
                    DiscoveryCandidatePriority.Environment));
        }

        return [];
    }

    protected override string ApplyConnectionParameters(string baseUrl, IDictionary<string, object> parameters) =>
        Normalize(baseUrl);

    protected override string? ReadAspireServiceDiscovery()
    {
        foreach (var service in _descriptor.AspireServiceNames)
        {
            var value = _configuration[$"services:{service}:default:0"];
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }

        return null;
    }

    private string Normalize(string value)
    {
        try
        {
            var uri = new Uri(value);
            var scheme = string.Equals(uri.Scheme, _descriptor.CustomScheme, StringComparison.OrdinalIgnoreCase)
                ? Uri.UriSchemeHttp
                : uri.Scheme;
            return $"{scheme}://{uri.Host}:{uri.Port}";
        }
        catch (Exception ex)
        {
            ReportNormalizationFailure(value, ex);
            return value;
        }
    }
}

internal sealed class SearchEngineHealthContributor<TOptions> : VectorAdapterHealthContributorBase
    where TOptions : SearchEngineVectorOptions
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IOptions<TOptions> _options;
    private readonly SearchEngineConnectorDescriptor _descriptor;
    private readonly ILogger? _logger;

    internal SearchEngineHealthContributor(
        IHttpClientFactory httpFactory,
        IOptions<TOptions> options,
        IVectorAdapterParticipation participation,
        SearchEngineConnectorDescriptor descriptor,
        ILogger? logger)
        : base(descriptor.ProviderId, participation)
    {
        _httpFactory = httpFactory;
        _options = options;
        _descriptor = descriptor;
        _logger = logger;
    }

    protected override async Task ProbeSource(string source, CancellationToken ct)
    {
        var http = _httpFactory.CreateClient(_descriptor.HttpClientName);
        SearchEngineHttp.Configure(http, _options.Value, _descriptor.ConfigurationName);
        var response = await http.GetAsync("/_cluster/health", ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"HTTP {(int)response.StatusCode}: {body}");
        KoanLog.HealthDebug(_logger, _descriptor.HealthLogCategory, "healthy", ("status", (int)response.StatusCode));
    }
}

internal static class SearchEngineHttp
{
    internal static void Configure(HttpClient http, ISearchEngineVectorOptions options, string engine)
    {
        if (string.IsNullOrWhiteSpace(options.Endpoint))
            throw new InvalidOperationException($"{engine} endpoint must be configured.");

        http.BaseAddress = new Uri(options.Endpoint);
        if (http.Timeout == default)
            http.Timeout = TimeSpan.FromSeconds(Math.Max(1, options.DefaultTimeoutSeconds));

        if (!string.IsNullOrEmpty(options.ApiKey))
        {
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("ApiKey", options.ApiKey);
        }
        else if (!string.IsNullOrEmpty(options.Username))
        {
            var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.Username}:{options.Password ?? ""}"));
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        }
    }
}
