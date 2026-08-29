using Koan.Core;
using Koan.Core.Adapters;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using Koan.Data.Adapters.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Data.Connector.Firebird;

internal sealed class FirebirdOptionsConfigurator : AdapterOptionsConfigurator<FirebirdOptions>
{
    private readonly IServiceDiscoveryCoordinator? _discovery;
    protected override string ProviderName => "Firebird";

    public FirebirdOptionsConfigurator(
        IConfiguration configuration,
        ILogger<FirebirdOptionsConfigurator> logger,
        IOptions<AdaptersReadinessOptions> readiness,
        IServiceDiscoveryCoordinator? discovery = null)
        : base(configuration, logger, readiness) => _discovery = discovery;

    protected override void ConfigureProviderSpecific(FirebirdOptions options)
    {
        var configured = FirstConcrete(
            Configuration[Infrastructure.Constants.Configuration.ConnectionString],
            Configuration.GetConnectionString("Firebird"));
        options.ConnectionString = configured ?? Discover();
        options.Database = ReadProviderConfiguration(
            options.Database, Infrastructure.Constants.Configuration.Database);
    }

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
