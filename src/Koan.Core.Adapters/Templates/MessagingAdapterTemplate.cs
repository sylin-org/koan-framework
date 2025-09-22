using Koan.Orchestration.Models;

namespace Koan.Core.Adapters.Templates;

/// <summary>
/// Template for generating messaging/queue service adapter scaffolding.
/// Provides patterns for publish/subscribe, queues, and message routing.
/// </summary>
public class MessagingAdapterTemplate : IAdapterTemplate
{
    public string Name => "Messaging Adapter";
    public string Description => "Generates a messaging adapter with publish/subscribe, queues, and message routing capabilities";
    public ServiceType ServiceType => ServiceType.Messaging;

    public string GenerateCode(AdapterTemplateParameters parameters)
    {
        var usingStatements = string.Join(Environment.NewLine,
            new[] { "using Microsoft.Extensions.Configuration;", "using Microsoft.Extensions.Logging;", "using Koan.Core.Adapters;", "using Koan.Orchestration.Models;" }
            .Concat(parameters.UsingStatements)
            .Select(u => u.EndsWith(";") ? u : u + ";"));

        return $@"{usingStatements}

namespace {parameters.Namespace};

/// <summary>
/// {parameters.DisplayName} adapter implementation
/// </summary>
public class {parameters.ClassName} : BaseKoanAdapter
{{
    public override ServiceType ServiceType => ServiceType.{parameters.ServiceType};
    public override string AdapterId => ""{parameters.AdapterId}"";
    public override string DisplayName => ""{parameters.DisplayName}"";

    public override AdapterCapabilities Capabilities => AdapterCapabilities.Create()
        .WithHealth(HealthCapabilities.Basic | HealthCapabilities.ConnectionHealth | HealthCapabilities.ResponseTime)
        .WithConfiguration(ConfigurationCapabilities.EnvironmentVariables | ConfigurationCapabilities.ConfigurationFiles | ConfigurationCapabilities.ConnectionStrings)
        .WithMessaging(MessagingCapabilities.PublishSubscribe | MessagingCapabilities.Queues | MessagingCapabilities.Topics)
        .WithCustom(""message_routing"", ""durable_queues"", ""dead_letter"");

    public {parameters.ClassName}(ILogger<{parameters.ClassName}> logger, IConfiguration configuration)
        : base(logger, configuration)
    {{
    }}

    protected override async Task InitializeAdapterAsync(CancellationToken cancellationToken = default)
    {{
        var connectionString = GetConnectionString();
        if (string.IsNullOrEmpty(connectionString))
        {{
            throw new InvalidOperationException(""Connection string not configured for {parameters.DisplayName}"");
        }}

        Logger.LogInformation(""[{{AdapterId}}] Initializing {parameters.DisplayName} connection"", AdapterId);

        // TODO: Initialize messaging connection
        // Example: _connection = new MessagingConnection(connectionString);
        // await _connection.ConnectAsync(cancellationToken);

        Logger.LogInformation(""[{{AdapterId}}] {parameters.DisplayName} connection established"", AdapterId);
    }}

    protected override async Task<IReadOnlyDictionary<string, object?>?> CheckAdapterHealthAsync(CancellationToken cancellationToken = default)
    {{
        try
        {{
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // TODO: Implement health check
            // Example: await _connection.PingAsync(cancellationToken);

            stopwatch.Stop();

            var healthData = new Dictionary<string, object?>
            {{
                [""status""] = ""healthy"",
                [""response_time_ms""] = stopwatch.ElapsedMilliseconds,
                [""connection_string""] = GetConnectionString()?.Substring(0, Math.Min(20, GetConnectionString()?.Length ?? 0)) + ""..."",
                [""provider""] = ""{parameters.DisplayName}""
            }};

            // TODO: Add messaging-specific health metrics
            // Example:
            // try
            // {{
            //     healthData[""queues_count""] = await GetQueueCountAsync(cancellationToken);
            //     healthData[""active_connections""] = await GetActiveConnectionsAsync(cancellationToken);
            // }}
            // catch (Exception ex)
            // {{
            //     healthData[""metrics_error""] = ex.Message;
            // }}

            return healthData;
        }}
        catch (Exception ex)
        {{
            Logger.LogWarning(ex, ""[{{AdapterId}}] Health check failed"", AdapterId);
            return new Dictionary<string, object?>
            {{
                [""status""] = ""unhealthy"",
                [""error""] = ex.Message
            }};
        }}
    }}

    protected override Task<IReadOnlyDictionary<string, object?>?> GetAdapterBootstrapMetadataAsync(CancellationToken cancellationToken = default)
    {{
        var metadata = new Dictionary<string, object?>
        {{
            [""provider""] = ""{parameters.DisplayName}"",
            [""connection_configured""] = !string.IsNullOrEmpty(GetConnectionString()),
            [""adapter_type""] = ""messaging"",
            [""features""] = new[] {{ ""publish_subscribe"", ""queues"", ""topics"", ""routing"" }},
            [""capabilities""] = Capabilities.GetCapabilitySummary()
        }};

        return Task.FromResult<IReadOnlyDictionary<string, object?>?>(metadata);
    }}

    // TODO: Add messaging-specific methods
    // Example:
    // public async Task PublishAsync<T>(string topic, T message, CancellationToken cancellationToken = default)
    // {{
    //     // Implementation for publishing messages
    // }}
    //
    // public async Task SubscribeAsync<T>(string topic, Func<T, Task> handler, CancellationToken cancellationToken = default)
    // {{
    //     // Implementation for subscribing to messages
    // }}
    //
    // public async Task SendToQueueAsync<T>(string queueName, T message, CancellationToken cancellationToken = default)
    // {{
    //     // Implementation for sending to queue
    // }}
}}";
    }

    public AdapterTemplateDefinition GetTemplateDefinition()
    {
        return new AdapterTemplateDefinition
        {
            Name = Name,
            Description = Description,
            ServiceType = ServiceType,
            RequiredParameters = new List<string> { "ClassName", "AdapterId", "DisplayName", "Namespace" },
            OptionalParameters = new Dictionary<string, object>
            {
                { "IsCritical", true },
                { "Priority", 100 }
            },
            ParameterDescriptions = new Dictionary<string, string>
            {
                { "ClassName", "The class name for the adapter" },
                { "AdapterId", "Unique identifier for adapter registration" },
                { "DisplayName", "Human-readable display name" },
                { "Namespace", "Namespace for the generated class" }
            },
            ParameterExamples = new Dictionary<string, string>
            {
                { "ClassName", "RabbitMqAdapter" },
                { "AdapterId", "rabbitmq" },
                { "DisplayName", "RabbitMQ Message Broker" },
                { "Namespace", "MyApp.Messaging.RabbitMq" }
            }
        };
    }
}