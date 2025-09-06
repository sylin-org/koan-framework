using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Sora.Messaging.RabbitMq;

/// <summary>
/// RabbitMQ implementation of IMessagingProvider with auto-discovery and zero configuration.
/// </summary>
public class RabbitMqProvider : IMessagingProvider
{
    private readonly ILogger<RabbitMqProvider>? _logger;
    private string? _workingConnectionString;

    public RabbitMqProvider(ILogger<RabbitMqProvider>? logger = null)
    {
        _logger = logger;
    }

    public string Name => "RabbitMQ";
    public int Priority => 100; // High priority - preferred provider

    public async Task<bool> CanConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var connectionStrings = GeneratePossibleConnectionStrings();

            foreach (var connectionString in connectionStrings)
            {
                _logger?.LogDebug("[RabbitMQ] Trying connection: {ConnectionString}", MaskConnectionString(connectionString));
                if (await TryConnectAsync(connectionString, cancellationToken))
                {
                    _workingConnectionString = connectionString;
                    _logger?.LogDebug("[RabbitMQ] Connection successful: {ConnectionString}", MaskConnectionString(connectionString));
                    return true;
                }
                else
                {
                    _logger?.LogDebug("[RabbitMQ] Connection failed: {ConnectionString}", MaskConnectionString(connectionString));
                }
            }

            _logger?.LogDebug("[RabbitMQ] Not available - tried {Count} connection strings", connectionStrings.Count());
            return false;
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

    private IEnumerable<string> GeneratePossibleConnectionStrings()
    {
        // Check environment variables first
        if (Environment.GetEnvironmentVariable("RABBITMQ_URL") is string envUrl)
            yield return envUrl;

        if (Environment.GetEnvironmentVariable("SORA_RABBITMQ_URL") is string soraUrl)
            yield return soraUrl;

        // If no connection string is provided, use container-first logic
        if (Sora.Core.SoraEnv.InContainer)
        {
            // Try standardized container name (rabbitmq) with default port and user settings
            yield return "amqp://guest:guest@rabbitmq:5672";
        }
        else
        {
            // If not in container, try localhost
            yield return "amqp://guest:guest@localhost:5672";
        }
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
                channel.ExchangeDeclare("sora.test", ExchangeType.Direct, durable: false, autoDelete: true);
                channel.ExchangeDelete("sora.test");
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

        // Distinguish wrapper vs plain entity messages to avoid shape collision.
        // Previously FlowTargetedMessage<T> shared the same queue name as T causing
        // deserialization of plain T JSON into FlowTargetedMessage<T> (Entity null -> Send(null) -> ArgumentNullException).
        if (type.IsGenericType && type.GetGenericTypeDefinition().Name.StartsWith("FlowTargetedMessage"))
        {
            var innerType = type.GetGenericArguments()[0];
            var inner = innerType.FullName ?? innerType.Name;
            return inner + ".targeted"; // generic, non-domain specific suffix
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

            _logger?.LogDebug("🎧 RabbitMQ consumer started for queue {QueueName}", _queueName);
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