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
internal sealed class RabbitMqOrchestrationEvaluator(
    KoanApplicationReferenceManifest references,
    ILogger<RabbitMqOrchestrationEvaluator>? logger = null)
    : BaseOrchestrationEvaluator(logger)
{
    public override string ServiceName => Constants.Broker.ServiceName;
    public override int StartupPriority => Constants.Broker.StartupPriority;

    protected override bool IsServiceEnabled(IConfiguration configuration)
        => HasExplicitConfiguration(configuration)
           || references.Contains(KoanReferenceKind.Package, Constants.PackageReference)
           || references.Contains(KoanReferenceKind.Project, Constants.ProjectReference);

    protected override bool HasExplicitConfiguration(IConfiguration configuration)
        => !string.IsNullOrWhiteSpace(
            configuration.GetConnectionString(Constants.Configuration.ConnectionStringName));

    protected override int GetDefaultPort() => Constants.Broker.AmqpPort;

    protected override string[] GetAdditionalHostCandidates(IConfiguration configuration)
        => new[]
            {
                Environment.GetEnvironmentVariable(Constants.Broker.EnvironmentUrl)
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
            var endpoint = hostResult.HostEndpoint ?? $"localhost:{Constants.Broker.AmqpPort}";
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
            Image = Constants.Broker.ContainerImageReference,
            Port = Constants.Broker.AmqpPort,
            Ports = new Dictionary<int, int>
            {
                [Constants.Broker.AmqpPort] = Constants.Broker.AmqpPort,
                [Constants.Broker.ManagementPort] = Constants.Broker.ManagementPort
            },
            StartupPriority = StartupPriority,
            HealthTimeout = Constants.Broker.OrchestrationHealthTimeout,
            HealthCheckCommand = Constants.Broker.HealthCheckCommand,
            Environment = new Dictionary<string, string>(context.EnvironmentVariables)
            {
                [Constants.Broker.DependencyTypeEnvironment] = Constants.Broker.ServiceName,
                [Constants.Broker.DefaultUserEnvironment] = username,
                [Constants.Broker.DefaultPasswordEnvironment] = password
            },
            Volumes = [$"koan-rabbitmq-{context.SessionId}:{Constants.Broker.DataPath}"]
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
