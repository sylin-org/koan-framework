using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using Koan.Redis.Initialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Koan.Redis.Discovery;

/// <summary>Discovers and validates the shared Redis backend independently of any consuming pillar.</summary>
public sealed class RedisDiscoveryAdapter(
    IConfiguration configuration,
    ILogger<RedisDiscoveryAdapter> logger)
    : ServiceDiscoveryAdapterBase(configuration, logger)
{
    public override string ServiceName => Infrastructure.Constants.Discovery.ServiceName;
    public override string[] Aliases => ["cache"];

    protected override Type GetFactoryType() => typeof(RedisModule);

    protected override async Task<bool> ValidateServiceHealth(
        string serviceUrl,
        DiscoveryContext context,
        CancellationToken cancellationToken)
    {
        var options = ConfigurationOptions.Parse(serviceUrl);
        options.ConnectTimeout = (int)context.HealthCheckTimeout.TotalMilliseconds;
        options.SyncTimeout = (int)context.HealthCheckTimeout.TotalMilliseconds;
        options.AbortOnConnectFail = true;

        using var connection = await ConnectionMultiplexer.ConnectAsync(options).ConfigureAwait(false);
        await connection.GetDatabase().PingAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    protected override string? ReadExplicitConfiguration()
        => Options.RedisOptionsConfigurator.ReadExplicitConnection(_configuration);

    protected override IEnumerable<DiscoveryCandidate> GetEnvironmentCandidates()
    {
        var values = Environment.GetEnvironmentVariable("REDIS_URLS") ??
                     Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRINGS");
        return string.IsNullOrWhiteSpace(values)
            ? []
            : values.Split([';', ','], StringSplitOptions.RemoveEmptyEntries)
                .Select(static value => new DiscoveryCandidate(
                    value.Trim(),
                    "environment-redis-urls",
                    DiscoveryCandidatePriority.Environment));
    }

    protected override string ApplyConnectionParameters(string baseUrl, IDictionary<string, object> parameters)
    {
        var options = ConfigurationOptions.Parse(baseUrl);
        if (parameters.TryGetValue("password", out var password))
            options.Password = Convert.ToString(password);
        if (parameters.TryGetValue("database", out var database) && int.TryParse(Convert.ToString(database), out var db))
            options.DefaultDatabase = db;
        return options.ToString(includePassword: true);
    }

    protected override string? ReadAspireServiceDiscovery()
        => _configuration["services:redis:default:0"];
}
