using Koan.Core;
using Koan.Core.Adapters;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using Koan.Data.Adapters.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Data.Connector.CouchDb;

internal sealed class CouchDbOptionsConfigurator : AdapterOptionsConfigurator<CouchDbOptions>
{
    private readonly IServiceDiscoveryCoordinator? _discovery;
    protected override string ProviderName => "CouchDb";

    public CouchDbOptionsConfigurator(
        IConfiguration configuration,
        ILogger<CouchDbOptionsConfigurator> logger,
        IOptions<AdaptersReadinessOptions> readiness,
        IServiceDiscoveryCoordinator? discovery = null)
        : base(configuration, logger, readiness) => _discovery = discovery;

    protected override void ConfigureProviderSpecific(CouchDbOptions options)
    {
        var configured = FirstConcrete(
            Configuration[Infrastructure.Constants.Configuration.Endpoint],
            Configuration.GetConnectionString("CouchDb"));
        options.Endpoint = configured ?? Discover();
        options.Database = ReadProviderConfiguration(
            options.Database, Infrastructure.Constants.Configuration.Database);
        // Credential layering, most specific wins: configuration keys, then the official image's
        // own environment convention (COUCHDB_USER/COUCHDB_PASSWORD - the operator typed them for
        // `docker run` already), then the Testcontainers/official-docs development default
        // admin/password. CouchDB 3.x refuses to start without an admin user, so "no credentials"
        // is not a viable default and zero configuration must still be able to connect.
        options.UserId = FirstNonEmpty(
            ReadProviderConfiguration(options.UserId, Infrastructure.Constants.Configuration.UserId),
            Environment.GetEnvironmentVariable("COUCHDB_USER"),
            Infrastructure.Constants.Configuration.DefaultUserId);
        options.Password = FirstNonEmpty(
            ReadProviderConfiguration(options.Password, Infrastructure.Constants.Configuration.Password),
            Environment.GetEnvironmentVariable("COUCHDB_PASSWORD"),
            Infrastructure.Constants.Configuration.DefaultPassword);
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));

    private string Discover()
    {
        if (_discovery is null) return "auto";
        try
        {
            var result = _discovery.DiscoverService(
                Infrastructure.Constants.Service,
                new DiscoveryContext
                {
                    OrchestrationMode = KoanEnv.OrchestrationMode,
                    HealthCheckTimeout = TimeSpan.FromMilliseconds(500)
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
        values.FirstOrDefault(static value => !IsAutomatic(value));

    private static bool IsAutomatic(string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        string.Equals(value.Trim(), "auto", StringComparison.OrdinalIgnoreCase);
}
