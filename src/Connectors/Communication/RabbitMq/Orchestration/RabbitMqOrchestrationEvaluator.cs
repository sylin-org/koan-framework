using Koan.Communication.Connector.RabbitMq.Infrastructure;
using Koan.Core;
using Koan.Core.Composition;
using Koan.Core.Orchestration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using Constants = Koan.Communication.Connector.RabbitMq.Infrastructure.Constants;

namespace Koan.Communication.Connector.RabbitMq.Orchestration;

/// <summary>Provisions RabbitMQ when the Communication connector is directly intended or explicitly configured.</summary>
public sealed class RabbitMqOrchestrationEvaluator(
    KoanApplicationReferenceManifest references,
    ILogger<RabbitMqOrchestrationEvaluator>? logger = null)
    : BaseOrchestrationEvaluator(logger)
{
    public override string ServiceName => Constants.Broker.ServiceName;
    public override int StartupPriority => 250;

    protected override bool IsServiceEnabled(IConfiguration configuration)
        => HasExplicitConfiguration(configuration)
           || references.Contains(KoanReferenceKind.Package, Constants.PackageReference)
           || references.Contains(KoanReferenceKind.Project, Constants.ProjectReference);

    protected override bool HasExplicitConfiguration(IConfiguration configuration)
        => !string.IsNullOrWhiteSpace(Configuration.ReadFirst(
            configuration,
            [
                Constants.Configuration.ConnectionString,
                Constants.Configuration.LegacyConnectionString,
                Constants.Configuration.LegacyFallbackConnectionString,
                "ConnectionStrings:rabbitmq",
                "ConnectionStrings:RabbitMQ"
            ]));

    protected override int GetDefaultPort() => 5672;

    protected override string[] GetAdditionalHostCandidates(IConfiguration configuration)
        => new[]
            {
                Environment.GetEnvironmentVariable(Constants.Broker.EnvironmentUrl),
                Environment.GetEnvironmentVariable(Constants.Broker.KoanEnvironmentUrl)
            }
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => GetHost(value!))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!)
            .ToArray();

    protected override async Task<bool> ValidateHostCredentials(
        IConfiguration configuration,
        HostDetectionResult hostResult)
    {
        try
        {
            var endpoint = hostResult.HostEndpoint ?? "localhost:5672";
            var username = configuration[Constants.Configuration.Username] ?? Constants.Broker.DefaultUsername;
            var password = configuration[Constants.Configuration.Password] ?? Constants.Broker.DefaultPassword;
            var factory = new ConnectionFactory { Uri = new Uri($"amqp://{username}:{password}@{endpoint}") };
            await using var connection = await factory.CreateConnectionAsync().ConfigureAwait(false);
            return connection.IsOpen;
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
        var username = configuration[Constants.Configuration.Username] ?? Constants.Broker.DefaultUsername;
        var password = configuration[Constants.Configuration.Password] ?? Constants.Broker.DefaultPassword;
        return Task.FromResult(new DependencyDescriptor
        {
            Name = ServiceName,
            Image = "rabbitmq:3.13-management",
            Port = 5672,
            Ports = new Dictionary<int, int> { [5672] = 5672, [15672] = 15672 },
            StartupPriority = StartupPriority,
            HealthTimeout = TimeSpan.FromSeconds(30),
            HealthCheckCommand = "rabbitmq-diagnostics -q ping",
            Environment = new Dictionary<string, string>(context.EnvironmentVariables)
            {
                ["KOAN_DEPENDENCY_TYPE"] = Constants.Broker.ServiceName,
                ["RABBITMQ_DEFAULT_USER"] = username,
                ["RABBITMQ_DEFAULT_PASS"] = password
            },
            Volumes = [$"koan-rabbitmq-{context.SessionId}:/var/lib/rabbitmq"]
        });
    }

    private static string? GetHost(string value)
    {
        var candidate = value.Contains("://", StringComparison.Ordinal)
            ? value
            : $"amqp://{value}";

        return Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            ? uri.Host
            : null;
    }
}
