using Koan.Core;
using Koan.Core.Adapters;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using Koan.Data.Adapters.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Data.Vector.Connector.PgVector;

internal sealed class PgVectorOptionsConfigurator : AdapterOptionsConfigurator<PgVectorOptions>
{
    private readonly IServiceDiscoveryCoordinator? _discovery;
    protected override string ProviderName => "PgVector";

    public PgVectorOptionsConfigurator(
        IConfiguration configuration,
        ILogger<PgVectorOptionsConfigurator> logger,
        IOptions<AdaptersReadinessOptions> readiness,
        IServiceDiscoveryCoordinator? discovery = null)
        : base(configuration, logger, readiness) => _discovery = discovery;

    protected override void ConfigureProviderSpecific(PgVectorOptions options)
    {
        var configured = FirstConcrete(
            Configuration[Infrastructure.Constants.Configuration.Keys.ConnectionString],
            Configuration.GetConnectionString("PgVector"));
        options.ConnectionString = configured ?? Discover();
        options.CommandTimeoutSeconds = ReadProviderConfiguration(
            options.CommandTimeoutSeconds,
            Infrastructure.Constants.Configuration.Keys.CommandTimeoutSeconds);
        options.MaxMetadataBytesPerPoint = ReadProviderConfiguration(
            options.MaxMetadataBytesPerPoint,
            Infrastructure.Constants.Configuration.Keys.MaxMetadataBytesPerPoint);
        options.MaxBatchPoints = ReadProviderConfiguration(
            options.MaxBatchPoints,
            Infrastructure.Constants.Configuration.Keys.MaxBatchPoints);
        options.MaxSearchCandidates = ReadProviderConfiguration(
            options.MaxSearchCandidates,
            Infrastructure.Constants.Configuration.Keys.MaxSearchCandidates);
    }

    private string Discover()
    {
        if (Koan.Core.Configuration.Read(
                Configuration,
                Infrastructure.Constants.Configuration.Keys.DisableAutoDetection,
                false) || _discovery is null)
            return Infrastructure.Constants.Configuration.Automatic;
        try
        {
            var result = _discovery.DiscoverService(
                Infrastructure.Constants.Provider.Name,
                new DiscoveryContext
                {
                    OrchestrationMode = KoanEnv.OrchestrationMode,
                    HealthCheckTimeout = TimeSpan.FromMilliseconds(500)
                }).GetAwaiter().GetResult();
            return result.IsSuccessful && !string.IsNullOrWhiteSpace(result.ServiceUrl)
                ? result.ServiceUrl
                : Infrastructure.Constants.Configuration.Automatic;
        }
        catch (Exception error)
        {
            LogDiscovery(LogLevel.Warning, "fallback", ("reason", error.GetType().Name));
            return Infrastructure.Constants.Configuration.Automatic;
        }
    }

    private static string? FirstConcrete(params string?[] values) =>
        values.FirstOrDefault(static value => !IsAutomatic(value));

    private static bool IsAutomatic(string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        string.Equals(value.Trim(), Infrastructure.Constants.Configuration.Automatic, StringComparison.OrdinalIgnoreCase);
}
