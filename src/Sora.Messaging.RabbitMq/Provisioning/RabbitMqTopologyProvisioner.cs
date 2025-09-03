using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RabbitMQ.Client;
using Sora.Messaging.Provisioning;

namespace Sora.Messaging.RabbitMq.Provisioning;

/// <summary>
/// RabbitMQ implementation of ITopologyProvisioner, using IModel for idempotent topology creation.
/// </summary>
public sealed class RabbitMqTopologyProvisioner : Sora.Messaging.Provisioning.IAdvancedTopologyProvisioner
{
    private readonly IConnection _connection;
    private readonly IModel _channel;

    public RabbitMqTopologyProvisioner(IConnection connection, IModel channel)
    {
        _connection = connection;
        _channel = channel;
    }

    public Task DeclareExchangeAsync(string name, Sora.Messaging.Provisioning.ExchangeType type = Sora.Messaging.Provisioning.ExchangeType.Topic, bool durable = true, bool autoDelete = false, CancellationToken ct = default)
    {
        var typeStr = type switch
        {
            Sora.Messaging.Provisioning.ExchangeType.Direct => "direct",
            Sora.Messaging.Provisioning.ExchangeType.Fanout => "fanout",
            Sora.Messaging.Provisioning.ExchangeType.Topic => "topic",
            Sora.Messaging.Provisioning.ExchangeType.Headers => "headers",
            _ => "topic"
        };
        _channel.ExchangeDeclare(name, typeStr, durable, autoDelete, arguments: null);
        return Task.CompletedTask;
    }

    public Task DeclareQueueAsync(string name, bool durable = true, bool exclusive = false, bool autoDelete = false, CancellationToken ct = default)
    {
        _channel.QueueDeclare(name, durable, exclusive, autoDelete, arguments: null);
        return Task.CompletedTask;
    }

    public Task DeclareQueueAsync(Sora.Messaging.Provisioning.QueueSpec spec, CancellationToken ct = default)
    {
        // Compose arguments including DLQ if specified (respect existing explicit argument keys)
        IDictionary<string, object?>? args = spec.Arguments is null ? new Dictionary<string, object?>() : new Dictionary<string, object?>(spec.Arguments);
        if (spec.Dlq?.ExchangeName is not null && !args.ContainsKey("x-dead-letter-exchange"))
        {
            args["x-dead-letter-exchange"] = spec.Dlq.ExchangeName;
            if (!string.IsNullOrWhiteSpace(spec.Dlq.RoutingKey))
                args["x-dead-letter-routing-key"] = spec.Dlq.RoutingKey;
        }
        // Retry buckets are not created here; those remain part of provider-specific advanced planner for now.
        var clientArgs = args.Count == 0
            ? null
            : args.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value!);
        _channel.QueueDeclare(spec.Name, spec.Durable, spec.Exclusive, spec.AutoDelete, clientArgs);
        return Task.CompletedTask;
    }

    public Task BindQueueAsync(string queue, string exchange, string routingKey, CancellationToken ct = default)
    {
        _channel.QueueBind(queue, exchange, routingKey);
        return Task.CompletedTask;
    }
}
