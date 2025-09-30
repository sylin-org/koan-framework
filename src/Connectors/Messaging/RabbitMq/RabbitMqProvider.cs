using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Koan.Core.Orchestration;

namespace Koan.Messaging.Connector.RabbitMq;

/// <summary>
/// RabbitMQ implementation of IMessagingProvider with orchestration-aware discovery.
/// </summary>
public class RabbitMqProvider : IMessagingProvider
{
    private readonly ILogger<RabbitMqProvider>? _logger;
    private readonly IConfiguration? _configuration;
    private string? _workingConnectionString;

    public RabbitMqProvider(ILogger<RabbitMqProvider>? logger = null, IConfiguration? configuration = null)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public string Name => "RabbitMQ";
    public int Priority => 100; // High priority - preferred provider

    public async Task<bool> CanConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Use orchestration-aware service discovery
            var connectionString = await GetOrchestrationAwareConnectionStringAsync(cancellationToken);

            _logger?.LogDebug("[RabbitMQ] Trying orchestration-aware connection: {ConnectionString}", MaskConnectionString(connectionString));
            if (await TryConnectAsync(connectionString, cancellationToken))
            {
                _workingConnectionString = connectionString;
                _logger?.LogDebug("[RabbitMQ] Connection successful: {ConnectionString}", MaskConnectionString(connectionString));
                return true;
            }
            else
            {
                _logger?.LogDebug("[RabbitMQ] Connection failed: {ConnectionString}", MaskConnectionString(connectionString));
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "[RabbitMQ] Connection check failed");
            return false;
        }
    }

    public async Task<IMessageBus> CreateBusAsync(CancellationToken cancellationToken = default)
    {
        if (_workingConnectionString == null)
        {
            throw new InvalidOperationException("Cannot create bus - no working connection string. Call CanConnectAsync first.");
        }

        return await Task.FromResult<IMessageBus>(new RabbitMqBus(_workingConnectionString, _logger));
    }

    private async Task<string> GetOrchestrationAwareConnectionStringAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Use centralized orchestration-aware service discovery
            var serviceDiscovery = new OrchestrationAwareServiceDiscovery(_configuration);

            // Create RabbitMQ-specific discovery options
            var discoveryOptions = ServiceDiscoveryExtensions.ForRabbitMQ();

            // Add legacy environment variable support for backward compatibility
            var envCandidates = GetLegacyEnvironmentCandidates();
            if (envCandidates.Length > 0)
            {
                discoveryOptions = discoveryOptions with
                {
                    AdditionalCandidates = envCandidates
                };
            }

            // Discover RabbitMQ service
            var result = await serviceDiscovery.DiscoverServiceAsync("rabbitmq", discoveryOptions, cancellationToken);

            _logger?.LogDebug("[RabbitMQ] Orchestration-aware discovery result: {Method} -> {ConnectionString}",
                result.DiscoveryMethod, MaskConnectionString(result.ServiceUrl));

            return result.ServiceUrl;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[RabbitMQ] Orchestration-aware discovery failed, falling back to localhost");
            return "amqp://guest:guest@localhost:5672";
        }
    }

    private string[] GetLegacyEnvironmentCandidates()
    {
        var candidates = new List<string>();

        // Check legacy environment variables for backward compatibility
        var envUrl = Environment.GetEnvironmentVariable("RABBITMQ_URL");
        if (!string.IsNullOrWhiteSpace(envUrl))
        {
            candidates.Add(envUrl);
        }

        var koanEnvUrl = Environment.GetEnvironmentVariable("Koan_RABBITMQ_URL");
        if (!string.IsNullOrWhiteSpace(koanEnvUrl))
        {
            candidates.Add(koanEnvUrl);
        }

        return candidates.ToArray();
    }

    private async Task<bool> TryConnectAsync(string connectionString, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Run(() =>
            {
                var factory = new ConnectionFactory { Uri = new Uri(connectionString) };
                using var connection = factory.CreateConnection();
                using var channel = connection.CreateModel();

                // Verify we can actually do basic operations
                channel.ExchangeDeclare("Koan.test", ExchangeType.Direct, durable: false, autoDelete: true);
                channel.ExchangeDelete("Koan.test");
            }, cancellationToken);

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string MaskConnectionString(string connectionString)
    {
        // Hide credentials for logging
        try
        {
            var uri = new Uri(connectionString);
            if (!string.IsNullOrEmpty(uri.UserInfo))
            {
                var maskedUserInfo = uri.UserInfo.Split(':')[0] + ":***";
                return connectionString.Replace(uri.UserInfo, maskedUserInfo);
            }
            return connectionString;
        }
        catch
        {
            return "***";
        }
    }
}

/// <summary>
/// RabbitMQ message bus implementation with convention-based routing.
/// </summary>
internal class RabbitMqBus : IMessageBus
{
    private readonly string _connectionString;
    private readonly ILogger? _logger;
    private IConnection? _connection;
    private readonly Dictionary<Type, RabbitMqConsumer> _consumers = new();

    public RabbitMqBus(string connectionString, ILogger? logger)
    {
        _connectionString = connectionString;
        _logger = logger;
    }

    public async Task SendAsync<T>(T message, CancellationToken cancellationToken = default) where T : class
    {
        await Task.Run(() =>
        {
            EnsureConnection();

            using var channel = _connection!.CreateModel();
            var queueName = GetQueueName<T>();

            // Ensure queue exists
            var queueResult = channel.QueueDeclare(queueName, durable: true, exclusive: false, autoDelete: false);

            // Serialize message
            var body = JsonSerializer.SerializeToUtf8Bytes(message);

            // Debug: log payload shape and preview
            var preview = body.Length > 256 ? System.Text.Encoding.UTF8.GetString(body, 0, 256) + "..." : System.Text.Encoding.UTF8.GetString(body);
            _logger?.LogDebug("[RabbitMQ] Sending payload to {QueueName}: {Preview}", queueName, preview);

            // Send message with persistence for guaranteed delivery
            var properties = channel.CreateBasicProperties();
            properties.Persistent = true; // Ensure message survives broker restarts

            channel.BasicPublish(
                exchange: "", // Use default exchange for direct routing
                routingKey: queueName,
                basicProperties: properties,
                body: body);

            _logger?.LogTrace("[RabbitMQ] Sent {MessageType} to queue {QueueName}", typeof(T).Name, queueName);
        }, cancellationToken);
    }

    public async Task<IMessageConsumer> CreateConsumerAsync<T>(Func<T, Task> handler, CancellationToken cancellationToken = default) where T : class
    {
        await Task.Run(() => EnsureConnection(), cancellationToken);

        var queueName = GetQueueName<T>();
        var consumer = new RabbitMqConsumer(_connection!, queueName, handler, _logger);

        await consumer.StartAsync(cancellationToken);

        _consumers[typeof(T)] = consumer;

        _logger?.LogInformation("[RabbitMQ] Consumer created for {MessageType} on queue {QueueName}", typeof(T).Name, queueName);

        return consumer;
    }

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await Task.Run(() => EnsureConnection(), cancellationToken);
            return _connection?.IsOpen == true;
        }
        catch
        {
            return false;
        }
    }

    private void EnsureConnection()
    {
        if (_connection?.IsOpen == true)
            return;

        var factory = new ConnectionFactory { Uri = new Uri(_connectionString) };
        _connection = factory.CreateConnection();

    }


    private static string GetQueueName<T>()
    {
        var type = typeof(T);

        // Handle transport envelope types distinctly from their payload types
        // to avoid queue name collisions and deserialization issues
        if (type.IsGenericType && 
            (type.GetGenericTypeDefinition().Name.StartsWith("TransportEnvelope") ||
             type.GetGenericTypeDefinition().Name.StartsWith("DynamicTransportEnvelope")))
        {
            var innerType = type.GetGenericArguments()[0];
            var inner = innerType.FullName ?? innerType.Name;
            return inner + ".transport"; // transport envelope suffix
        }

        // Default: use the concrete type's full name (fallback to Name if null)
        return type.FullName ?? type.Name;
    }
}

/// <summary>
/// RabbitMQ message consumer.
/// </summary>
internal class RabbitMqConsumer : IMessageConsumer
{
    private readonly IConnection _connection;
    private readonly string _queueName;
    private readonly object _handler;
    private readonly ILogger? _logger;
    private IModel? _channel;
    private EventingBasicConsumer? _consumer;

    public RabbitMqConsumer(IConnection connection, string queueName, object handler, ILogger? logger)
    {
        _connection = connection;
        _queueName = queueName;
        _handler = handler;
        _logger = logger;
        MessageType = _handler.GetType().GetGenericArguments()[0]; // Extract T from Func<T, Task>
    }

    public Type MessageType { get; }
    public string Destination => _queueName;
    public bool IsActive => _channel?.IsOpen == true;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            _channel = _connection.CreateModel();

            // Ensure queue exists
            _channel.QueueDeclare(_queueName, durable: true, exclusive: false, autoDelete: false);

            // Create consumer
            _consumer = new EventingBasicConsumer(_channel);
            _consumer.Received += (sender, args) =>
            {
                _ = Task.Run(async () => await HandleMessageAsync(sender, args));
            };

            _channel.BasicConsume(_queueName, autoAck: false, consumer: _consumer);

            _logger?.LogDebug("RabbitMQ consumer started for queue {QueueName}", _queueName);
        }, cancellationToken);
    }

    private async Task HandleMessageAsync(object? sender, BasicDeliverEventArgs args)
    {
        try
        {
            // Deserialize message
            var json = Encoding.UTF8.GetString(args.Body.ToArray());
            _logger?.LogDebug("[RabbitMQ] Received raw payload from {QueueName}: {Preview}", _queueName, json.Length > 256 ? json.Substring(0, 256) + "..." : json);
            var message = JsonSerializer.Deserialize(json, MessageType);

            // Debug: log deserialized type and null check
            _logger?.LogDebug("[RabbitMQ] Deserialized message type: {Type}, IsNull: {IsNull}", MessageType.FullName, message == null);

            // Invoke handler using reflection
            if (_handler is Delegate del)
            {
                var result = del.DynamicInvoke(message);
                if (result is Task task)
                    await task;
            }

            // Acknowledge message
            _channel!.BasicAck(args.DeliveryTag, multiple: false);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[RabbitMQ] Failed to handle message in queue {QueueName}", _queueName);

            // Reject message (will go to DLQ if configured)
            _channel!.BasicNack(args.DeliveryTag, multiple: false, requeue: false);
        }
    }

    public Task PauseAsync()
    {
        if (_channel != null && _consumer != null)
        {
            _channel.BasicCancel(_consumer.ConsumerTags.FirstOrDefault());
        }
        return Task.CompletedTask;
    }

    public Task ResumeAsync()
    {
        if (_channel != null)
        {
            _consumer = new EventingBasicConsumer(_channel);
            _consumer.Received += (sender, args) =>
            {
                _ = Task.Run(async () => await HandleMessageAsync(sender, args));
            };
            _channel.BasicConsume(_queueName, autoAck: false, consumer: _consumer);
        }
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _channel?.Dispose();
        return ValueTask.CompletedTask;
    }
}
