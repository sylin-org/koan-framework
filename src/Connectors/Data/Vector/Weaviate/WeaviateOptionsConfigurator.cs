using Koan.Core;
using Koan.Core.Adapters;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using Koan.Data.Adapters.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Data.Vector.Connector.Weaviate;

internal sealed class WeaviateOptionsConfigurator : AdapterOptionsConfigurator<WeaviateOptions>
{
    private readonly IServiceDiscoveryCoordinator? _discovery;
    protected override string ProviderName => "Weaviate";

    public WeaviateOptionsConfigurator(
        IConfiguration configuration,
        ILogger<WeaviateOptionsConfigurator> logger,
        IOptions<AdaptersReadinessOptions> readiness,
        IServiceDiscoveryCoordinator? discovery = null)
        : base(configuration, logger, readiness) => _discovery = discovery;

    protected override void ConfigureProviderSpecific(WeaviateOptions options)
    {
        var explicitEndpoint = Configuration[Infrastructure.Constants.Configuration.Keys.Endpoint];
        var legacyConnection = Configuration[Infrastructure.Constants.Configuration.Keys.LegacyConnectionString]
            ?? Configuration.GetConnectionString("Weaviate");
        options.Endpoint = !string.IsNullOrWhiteSpace(explicitEndpoint)
            ? explicitEndpoint
            : !string.IsNullOrWhiteSpace(legacyConnection) && !IsAutomatic(legacyConnection)
                ? legacyConnection
                : Discover();
        options.ApiKey = EmptyToNull(ReadProviderConfiguration(
            options.ApiKey ?? string.Empty, Infrastructure.Constants.Configuration.Keys.ApiKey));
        options.TimeoutSeconds = ReadProviderConfiguration(
            options.TimeoutSeconds, Infrastructure.Constants.Configuration.Keys.TimeoutSeconds);
        options.VisibilityTimeoutSeconds = ReadProviderConfiguration(
            options.VisibilityTimeoutSeconds, Infrastructure.Constants.Configuration.Keys.VisibilityTimeoutSeconds);
        options.MaxMetadataBytesPerPoint = ReadProviderConfiguration(
            options.MaxMetadataBytesPerPoint, Infrastructure.Constants.Configuration.Keys.MaxMetadataBytesPerPoint);
        options.MaxBatchPoints = ReadProviderConfiguration(
            options.MaxBatchPoints, Infrastructure.Constants.Configuration.Keys.MaxBatchPoints);
        options.MaxClearPoints = ReadProviderConfiguration(
            options.MaxClearPoints, Infrastructure.Constants.Configuration.Keys.MaxClearPoints);
        options.MaxSearchCandidates = ReadProviderConfiguration(
            options.MaxSearchCandidates, Infrastructure.Constants.Configuration.Keys.MaxSearchCandidates);
        options.MaxResponseBytes = ReadProviderConfiguration(
            options.MaxResponseBytes, Infrastructure.Constants.Configuration.Keys.MaxResponseBytes);
    }

    private string Discover()
    {
        if (Koan.Core.Configuration.Read(
                Configuration, Infrastructure.Constants.Configuration.Keys.DisableAutoDetection, false) || _discovery is null)
            return Infrastructure.Constants.Defaults.Endpoint;
        try
        {
            var result = _discovery.DiscoverService(Infrastructure.Constants.Provider.Name, new DiscoveryContext
            {
                OrchestrationMode = KoanEnv.OrchestrationMode,
                HealthCheckTimeout = TimeSpan.FromMilliseconds(500)
            }).GetAwaiter().GetResult();
            return result.IsSuccessful && !string.IsNullOrWhiteSpace(result.ServiceUrl)
                ? result.ServiceUrl
                : Infrastructure.Constants.Defaults.Endpoint;
        }
        catch (Exception error)
        {
            LogDiscovery(LogLevel.Warning, "fallback", ("reason", error.GetType().Name));
            return Infrastructure.Constants.Defaults.Endpoint;
        }
    }

    private static bool IsAutomatic(string value) =>
        string.Equals(value.Trim(), Infrastructure.Constants.Configuration.Automatic, StringComparison.OrdinalIgnoreCase);
    private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
