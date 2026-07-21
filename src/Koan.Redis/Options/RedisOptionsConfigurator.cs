using Koan.Core;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Koan.Redis.Options;

internal sealed class RedisOptionsConfigurator : IConfigureOptions<RedisOptions>
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<RedisOptionsConfigurator> _logger;
    private readonly IServiceDiscoveryCoordinator? _discovery;

    public RedisOptionsConfigurator(
        IConfiguration configuration,
        ILogger<RedisOptionsConfigurator> logger,
        IServiceDiscoveryCoordinator? discovery = null)
    {
        _configuration = configuration;
        _logger = logger;
        _discovery = discovery;
    }

    internal RedisOptionsConfigurator(IConfiguration configuration)
        : this(configuration, NullLogger<RedisOptionsConfigurator>.Instance)
    {
    }

    public void Configure(RedisOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.DisableAutoDetection = _configuration.GetValue(
            Infrastructure.Constants.Configuration.DisableAutoDetection,
            options.DisableAutoDetection);

        var configured = ReadExplicitConnection(_configuration);
        if (!IsAuto(configured))
        {
            options.ConnectionString = configured!.Trim();
            return;
        }

        if (!IsAuto(options.ConnectionString))
        {
            options.ConnectionString = options.ConnectionString.Trim();
            return;
        }

        options.ConnectionString = options.DisableAutoDetection
            ? DefaultEndpoint()
            : DiscoverOrDefault();
    }

    internal static string? ReadExplicitConnection(IConfiguration configuration)
    {
        var candidates = new[]
        {
            configuration.GetConnectionString("Redis"),
            configuration[Infrastructure.Constants.Configuration.ConnectionString],
            configuration[Infrastructure.Constants.Discovery.RedisUrl],
            configuration[Infrastructure.Constants.Discovery.RedisConnectionString]
        };

        return candidates.FirstOrDefault(static value => !IsAuto(value));
    }

    internal static bool HasExplicitConnection(IConfiguration configuration)
        => !IsAuto(ReadExplicitConnection(configuration));

    private string DiscoverOrDefault()
    {
        if (_discovery is null)
            return DefaultEndpoint();

        try
        {
            var context = new DiscoveryContext
            {
                OrchestrationMode = KoanEnv.OrchestrationMode,
                HealthCheckTimeout = TimeSpan.FromMilliseconds(500)
            };
            var result = _discovery
                .DiscoverService(Infrastructure.Constants.Discovery.ServiceName, context)
                .GetAwaiter()
                .GetResult();
            if (result.IsSuccessful && !string.IsNullOrWhiteSpace(result.ServiceUrl))
                return result.ServiceUrl;

            _logger.LogDebug(
                "Redis discovery did not resolve an endpoint; using {Fallback}. Reason: {Reason}",
                DefaultEndpoint(),
                result.ErrorMessage);
        }
        catch (Exception error)
        {
            _logger.LogDebug(error, "Redis discovery failed; using {Fallback}.", DefaultEndpoint());
        }

        return DefaultEndpoint();
    }

    private static string DefaultEndpoint()
        => KoanEnv.InContainer
            ? Infrastructure.Constants.Discovery.DefaultContainer
            : Infrastructure.Constants.Discovery.DefaultLocal;

    private static bool IsAuto(string? value)
        => string.IsNullOrWhiteSpace(value) || string.Equals(value.Trim(), "auto", StringComparison.OrdinalIgnoreCase);
}
