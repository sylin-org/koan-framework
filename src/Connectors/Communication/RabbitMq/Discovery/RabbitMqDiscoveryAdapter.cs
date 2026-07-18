using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using Constants = Koan.Communication.Connector.RabbitMq.Infrastructure.Constants;

namespace Koan.Communication.Connector.RabbitMq.Discovery;

/// <summary>Canonical RabbitMQ discovery, including an actual AMQP connection probe.</summary>
internal sealed class RabbitMqDiscoveryAdapter(
    IConfiguration configuration,
    ILogger<RabbitMqDiscoveryAdapter> logger)
    : ServiceDiscoveryAdapterBase(configuration, logger)
{
    public override string ServiceName => Constants.Broker.ServiceName;

    protected override Type GetFactoryType() => typeof(RabbitMqServiceDescriptor);

    protected override async Task<bool> ValidateServiceHealth(
        string serviceUrl,
        DiscoveryContext context,
        CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory { Uri = new Uri(serviceUrl) };
        await using var connection = await factory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        return connection.IsOpen;
    }

    protected override string? ReadExplicitConfiguration()
        => Concrete(_configuration.GetConnectionString(Constants.Configuration.ConnectionStringName));

    protected override IEnumerable<DiscoveryCandidate> GetEnvironmentCandidates()
    {
        var candidates = new List<DiscoveryCandidate>();
        Add(candidates, Environment.GetEnvironmentVariable(Constants.Broker.EnvironmentUrl), "environment-rabbitmq-url");
        return candidates;
    }

    protected override string? ReadAspireServiceDiscovery()
        => _configuration["services:rabbitmq:default:0"];

    protected override string ApplyConnectionParameters(string baseUrl, IDictionary<string, object> parameters)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
            || !string.IsNullOrWhiteSpace(uri.UserInfo))
            return baseUrl;

        var username = _configuration[Constants.Configuration.Username] ?? Constants.Broker.DefaultUsername;
        var password = _configuration[Constants.Configuration.Password] ?? Constants.Broker.DefaultPassword;
        var builder = new UriBuilder(uri)
        {
            UserName = username,
            Password = password
        };
        return builder.Uri.ToString();
    }

    private static string? Concrete(string? value)
        => string.Equals(value?.Trim(), "auto", StringComparison.OrdinalIgnoreCase) ? null : value;

    private static void Add(ICollection<DiscoveryCandidate> candidates, string? value, string method)
    {
        if (!string.IsNullOrWhiteSpace(value))
            candidates.Add(new DiscoveryCandidate(value, method, DiscoveryCandidatePriority.Environment));
    }
}
