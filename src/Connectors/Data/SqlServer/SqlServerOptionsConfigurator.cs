using Koan.Core;
using Koan.Core.Adapters;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using Koan.Data.Adapters.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Data.Connector.SqlServer;

internal sealed class SqlServerOptionsConfigurator : AdapterOptionsConfigurator<SqlServerOptions>
{
    private readonly IServiceDiscoveryCoordinator? _discovery;
    protected override string ProviderName => "SqlServer";

    public SqlServerOptionsConfigurator(
        IConfiguration configuration,
        ILogger<SqlServerOptionsConfigurator> logger,
        IOptions<AdaptersReadinessOptions> readiness,
        IServiceDiscoveryCoordinator? discovery = null)
        : base(configuration, logger, readiness) => _discovery = discovery;

    protected override void ConfigureProviderSpecific(SqlServerOptions options)
    {
        var configured = FirstConcrete(
            Configuration[Infrastructure.Constants.Configuration.ConnectionString],
            Configuration.GetConnectionString("SqlServer"));
        options.ConnectionString = configured ?? Discover();
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
                    HealthCheckTimeout = TimeSpan.FromSeconds(30) // SQL Server login latency is wildly variable (150ms-15s observed); the one-time boot probe must tolerate it
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
