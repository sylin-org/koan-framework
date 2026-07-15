using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using Koan.Messaging.Connector.RabbitMq.Infrastructure;

namespace Koan.Messaging.Connector.RabbitMq.Discovery;

/// <summary>
/// RabbitMQ autonomous discovery adapter (ARCH-0087).
/// Contains ALL RabbitMQ-specific discovery knowledge — core orchestration knows nothing about RabbitMQ.
/// Reads its own <see cref="Koan.Orchestration.Attributes.KoanServiceAttribute"/> (via
/// <see cref="RabbitMqServiceDescriptor"/>) for host/port/scheme and validates with a real AMQP connect.
/// </summary>
public sealed class RabbitMqDiscoveryAdapter : ServiceDiscoveryAdapterBase
{
    public override string ServiceName => "rabbitmq";

    public RabbitMqDiscoveryAdapter(IConfiguration configuration, ILogger<RabbitMqDiscoveryAdapter> logger)
        : base(configuration, logger) { }

    /// <summary>RabbitMQ adapter knows which descriptor carries its KoanServiceAttribute.</summary>
    protected override Type GetFactoryType() => typeof(RabbitMqServiceDescriptor);

    /// <summary>RabbitMQ-specific health validation: open and dispose a real AMQP connection.</summary>
    protected override async Task<bool> ValidateServiceHealth(string serviceUrl, DiscoveryContext context, CancellationToken cancellationToken)
    {
        try
        {
            var factory = new ConnectionFactory { Uri = new Uri(serviceUrl) };
            await using var connection = await factory.CreateConnectionAsync(cancellationToken);

            _logger.LogDebug("RabbitMQ health check passed for {Url}", serviceUrl);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug("RabbitMQ health check failed for {Url}: {Error}", serviceUrl, ex.Message);
            return false;
        }
    }

    /// <summary>RabbitMQ adapter reads its own configuration sections for explicit overrides.</summary>
    protected override string? ReadExplicitConfiguration()
    {
        return _configuration[ConfigurationConstants.Keys.ConnectionString] ??
               _configuration[ConfigurationConstants.Fallbacks.ConnectionString] ??
               _configuration.GetConnectionString("rabbitmq") ??
               _configuration.GetConnectionString("RabbitMQ");
    }

    /// <summary>Legacy environment-variable support preserved from V1 (RABBITMQ_URL / Koan_RABBITMQ_URL).</summary>
    protected override IEnumerable<DiscoveryCandidate> GetEnvironmentCandidates()
    {
        var candidates = new List<DiscoveryCandidate>();

        var envUrl = Environment.GetEnvironmentVariable("RABBITMQ_URL");
        if (!string.IsNullOrWhiteSpace(envUrl))
        {
            candidates.Add(new DiscoveryCandidate(envUrl, "environment-rabbitmq-url", DiscoveryCandidatePriority.Environment));
        }

        var koanEnvUrl = Environment.GetEnvironmentVariable("Koan_RABBITMQ_URL");
        if (!string.IsNullOrWhiteSpace(koanEnvUrl))
        {
            candidates.Add(new DiscoveryCandidate(koanEnvUrl, "environment-koan-rabbitmq-url", DiscoveryCandidatePriority.Environment));
        }

        return candidates;
    }

    /// <summary>RabbitMQ adapter handles Aspire service discovery for the broker.</summary>
    protected override string? ReadAspireServiceDiscovery()
    {
        return _configuration["services:rabbitmq:default:0"];
    }
}
