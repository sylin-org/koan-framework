using Koan.Core.Orchestration;
using Koan.Redis.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Koan.Redis.Orchestration;

/// <summary>Describes how the shared Redis backend can be supplied by Koan orchestration.</summary>
public sealed class RedisOrchestrationEvaluator(ILogger<RedisOrchestrationEvaluator>? logger = null)
    : BaseOrchestrationEvaluator(logger)
{
    public override string ServiceName => Infrastructure.Constants.Discovery.ServiceName;
    public override int StartupPriority => 300;

    protected override bool IsServiceEnabled(IConfiguration configuration)
        => HasExplicitConfiguration(configuration);

    protected override bool HasExplicitConfiguration(IConfiguration configuration)
        => RedisOptionsConfigurator.HasExplicitConnection(configuration);

    protected override int GetDefaultPort() => 6379;

    protected override string[] GetAdditionalHostCandidates(IConfiguration configuration)
    {
        var configured = RedisOptionsConfigurator.ReadExplicitConnection(configuration);
        if (string.IsNullOrWhiteSpace(configured)) return [];

        try
        {
            var options = ConfigurationOptions.Parse(configured);
            return options.EndPoints.Select(static endpoint => endpoint.ToString() ?? "").Where(static value => value.Length > 0).ToArray();
        }
        catch
        {
            return [];
        }
    }

    protected override async Task<bool> ValidateHostCredentials(
        IConfiguration configuration,
        HostDetectionResult hostResult)
    {
        if (string.IsNullOrWhiteSpace(hostResult.HostEndpoint)) return false;

        try
        {
            var configured = RedisOptionsConfigurator.ReadExplicitConnection(configuration);
            var options = string.IsNullOrWhiteSpace(configured)
                ? new ConfigurationOptions()
                : ConfigurationOptions.Parse(configured);
            options.EndPoints.Clear();
            options.EndPoints.Add(hostResult.HostEndpoint);
            options.AbortOnConnectFail = true;
            options.ConnectTimeout = 1000;

            using var connection = await ConnectionMultiplexer.ConnectAsync(options).ConfigureAwait(false);
            _ = await connection.GetDatabase().PingAsync().ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    protected override Task<DependencyDescriptor> CreateDependencyDescriptor(
        IConfiguration configuration,
        OrchestrationContext context)
    {
        var environment = new Dictionary<string, string>(context.EnvironmentVariables)
        {
            ["KOAN_DEPENDENCY_TYPE"] = "redis"
        };
        var configured = RedisOptionsConfigurator.ReadExplicitConnection(configuration);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            try
            {
                var options = ConfigurationOptions.Parse(configured);
                if (!string.IsNullOrWhiteSpace(options.Password))
                    environment["REDIS_PASSWORD"] = options.Password;
            }
            catch
            {
                // Configuration validation remains the connection owner's corrective boundary.
            }
        }

        return Task.FromResult(new DependencyDescriptor
        {
            Name = ServiceName,
            Image = "redis:7-alpine",
            Port = GetDefaultPort(),
            StartupPriority = StartupPriority,
            HealthTimeout = TimeSpan.FromSeconds(30),
            HealthCheckCommand = "redis-cli ping",
            Environment = environment,
            Volumes = [$"koan-redis-{context.SessionId}:/data"]
        });
    }
}
