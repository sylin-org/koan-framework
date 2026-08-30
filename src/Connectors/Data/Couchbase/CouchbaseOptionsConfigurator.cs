using Koan.Core;
using Koan.Core.Adapters;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using Koan.Data.Adapters.Configuration;
using Koan.Data.Connector.Couchbase.Discovery;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Data.Connector.Couchbase;

internal sealed class CouchbaseOptionsConfigurator : AdapterOptionsConfigurator<CouchbaseOptions>
{
    private readonly IServiceDiscoveryCoordinator? _discovery;
    protected override string ProviderName => "Couchbase";

    public CouchbaseOptionsConfigurator(
        IConfiguration configuration,
        ILogger<CouchbaseOptionsConfigurator> logger,
        IOptions<AdaptersReadinessOptions> readiness,
        IServiceDiscoveryCoordinator? discovery = null)
        : base(configuration, logger, readiness) => _discovery = discovery;

    protected override void ConfigureProviderSpecific(CouchbaseOptions options)
    {
        var configured = FirstConcrete(
            Configuration[Infrastructure.Constants.Configuration.ConnectionString],
            Configuration.GetConnectionString("Couchbase"));
        options.ConnectionString = configured ?? Discover();
        options.Bucket = ReadProviderConfiguration(
            options.Bucket, Infrastructure.Constants.Configuration.Bucket);
        options.Username = ReadProviderConfiguration(
            options.Username, Infrastructure.Constants.Configuration.Username);
        options.Password = ReadProviderConfiguration(
            options.Password, Infrastructure.Constants.Configuration.Password);
    }

    private string Discover()
    {
        if (_discovery is null) return "auto";
        try
        {
            var result = _discovery.DiscoverService(
                Infrastructure.Constants.Discovery.ServiceName,
                new DiscoveryContext
                {
                    OrchestrationMode = KoanEnv.OrchestrationMode,
                    HealthCheckTimeout = TimeSpan.FromSeconds(5)
                }).GetAwaiter().GetResult();
            return result.IsSuccessful && !string.IsNullOrWhiteSpace(result.ServiceUrl)
                ? result.ServiceUrl
                : "auto";
        }
        catch (Exception error)
        {
            LogDiscovery(LogLevel.Warning, "fallback", ("reason", error.GetType().Name));
            return "auto";
        }
    }

    private static string? FirstConcrete(params string?[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
}
