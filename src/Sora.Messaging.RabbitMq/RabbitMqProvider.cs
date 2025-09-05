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
                if (await TryConnectAsync(connectionString, cancellationToken))
                {
                    _workingConnectionString = connectionString;
                    _logger?.LogDebug("🐰 RabbitMQ connection successful: {ConnectionString}", MaskConnectionString(connectionString));
                    return true;
                }
            }
            
            _logger?.LogDebug("🐰 RabbitMQ not available - tried {Count} connection strings", connectionStrings.Count());
            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "🐰 RabbitMQ connection check failed");
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
            Console.WriteLine($"🏗️ SEND DEBUG: Ensured queue '{queueName}' exists (messages: {queueResult.MessageCount}, consumers: {queueResult.ConsumerCount})");
            
            // Serialize message
            var body = JsonSerializer.SerializeToUtf8Bytes(message);
            
            // Send message with persistence for guaranteed delivery
            var properties = channel.CreateBasicProperties();
            properties.Persistent = true; // Ensure message survives broker restarts
            
            channel.BasicPublish(
                exchange: "", // Use default exchange for direct routing
                routingKey: queueName,
                basicProperties: properties,
                body: body);
            
            _logger?.LogTrace("📤 Sent {MessageType} to queue {QueueName}", typeof(T).Name, queueName);
        }, cancellationToken);
    }
    
    public async Task<IMessageConsumer> CreateConsumerAsync<T>(Func<T, Task> handler, CancellationToken cancellationToken = default) where T : class
    {
        await Task.Run(() => EnsureConnection(), cancellationToken);
        
        var queueName = GetQueueName<T>();
        var consumer = new RabbitMqConsumer(_connection!, queueName, handler, _logger);
        
        await consumer.StartAsync(cancellationToken);
        
        _consumers[typeof(T)] = consumer;
        
        Console.WriteLine($"🎯 CONSUMER DEBUG: Created consumer for {typeof(T).Name} on queue '{queueName}' at {DateTime.Now:HH:mm:ss.fff}");
        _logger?.LogInformation("👂 Created consumer for {MessageType} on queue {QueueName}", typeof(T).Name, queueName);
        
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
        
        // Pre-create all expected queues for guaranteed delivery
        PreCreateExpectedQueues();
    }
    
    private void PreCreateExpectedQueues()
    {
        Console.WriteLine("🚀 QUEUE INIT: Pre-creating all expected queues for guaranteed delivery...");
        
        using var channel = _connection!.CreateModel();
        
        // Pre-create queues for known Flow message types
        var expectedQueues = new[]
        {
            "Sora.Flow.FlowCommandMessage",
            "Sora.Flow.S8.Flow.Shared.Reading", 
            "Sora.Flow.S8.Flow.Shared.Device",
            "Sora.Flow.S8.Flow.Shared.Sensor"
        };
        
        foreach (var queueName in expectedQueues)
        {
            var queueResult = channel.QueueDeclare(queueName, durable: true, exclusive: false, autoDelete: false);
            Console.WriteLine($"✅ QUEUE INIT: Pre-created queue '{queueName}' (messages: {queueResult.MessageCount})");
        }
        
        Console.WriteLine($"🎯 QUEUE INIT: All {expectedQueues.Length} queues pre-created successfully!");
    }
    
    private static string GetQueueName<T>()
    {
        var type = typeof(T);
        string queueName;
        
        // Special handling for FlowTargetedMessage<T> - use inner type name
        if (type.IsGenericType && type.GetGenericTypeDefinition().Name.StartsWith("FlowTargetedMessage"))
        {
            var innerType = type.GetGenericArguments()[0];
            queueName = $"Sora.Flow.{innerType.FullName ?? innerType.Name}";
            Console.WriteLine($"🏷️  QUEUE DEBUG: {type.Name} -> Queue: {queueName}");
        }
        else
        {
            // Default: Full type name becomes queue name
            queueName = type.FullName ?? type.Name;
            Console.WriteLine($"🏷️  QUEUE DEBUG: {type.Name} -> Queue: {queueName}");
        }
        
        return queueName;
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
            var message = JsonSerializer.Deserialize(json, MessageType);
            
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
            _logger?.LogError(ex, "💥 Failed to handle message in queue {QueueName}", _queueName);
            
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