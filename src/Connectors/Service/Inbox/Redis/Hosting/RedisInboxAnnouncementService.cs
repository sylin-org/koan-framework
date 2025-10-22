using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

using Koan.Service.Inbox.Connector.Redis.Options;

namespace Koan.Service.Inbox.Connector.Redis.Hosting;

internal sealed class RedisInboxAnnouncementService : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<RedisInboxAnnouncementService> _logger;
    private readonly RedisInboxOptions _options;
    private readonly IHostEnvironment _environment;

    private IConnection? _connection;
    private IChannel? _channel;

    public RedisInboxAnnouncementService(
        IConfiguration configuration,
        ILogger<RedisInboxAnnouncementService> logger,
        IOptions<RedisInboxOptions> optionsAccessor,
        IHostEnvironment environment)
    {
        _configuration = configuration;
        _logger = logger;
        _options = optionsAccessor.Value;
        _environment = environment;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var rabbitConnection = ResolveRabbitConnectionString();
        if (string.IsNullOrWhiteSpace(rabbitConnection))
        {
            _logger.LogDebug("RabbitMQ connection string not configured; skipping inbox announce loop.");
            return;
        }

        try
        {
            var factory = new ConnectionFactory
            {
                Uri = new Uri(rabbitConnection),
                AutomaticRecoveryEnabled = true,
                TopologyRecoveryEnabled = true
            };

            _connection = await factory.CreateConnectionAsync(stoppingToken);
            _channel = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);

            var exchange = ResolveExchange();
            await _channel.ExchangeDeclareAsync(exchange, type: "topic", durable: true, autoDelete: false, arguments: null, cancellationToken: stoppingToken);

            var queue = (await _channel.QueueDeclareAsync(queue: string.Empty, durable: false, exclusive: true, autoDelete: true, arguments: null, cancellationToken: stoppingToken)).QueueName;
            var busCode = _configuration["Koan:Messaging:DefaultBus"] ?? "rabbit";
            var group = _configuration["Koan:Messaging:DefaultGroup"] ?? "workers";
            var routingKey = $"Koan.discovery.ping.{busCode}.{group}";

            await _channel.QueueBindAsync(queue, exchange, routingKey, cancellationToken: stoppingToken);

            var consumer = new AsyncEventingBasicConsumer(_channel);
            consumer.ReceivedAsync += async (_, ea) =>
            {
                try
                {
                    var replyTo = ea.BasicProperties?.ReplyTo;
                    var correlationId = ea.BasicProperties?.CorrelationId;
                    if (!string.IsNullOrWhiteSpace(replyTo))
                    {
                        var payload = JsonConvert.SerializeObject(new
                        {
                            endpoint = ResolveEndpoint(),
                            kind = "inbox",
                            name = "redis",
                            version = "v1"
                        });

                        var props = new BasicProperties
                        {
                            CorrelationId = correlationId,
                            ContentType = "application/json"
                        };

                        await _channel.BasicPublishAsync(
                            exchange: exchange,
                            routingKey: replyTo!,
                            mandatory: false,
                            basicProperties: props,
                            body: System.Text.Encoding.UTF8.GetBytes(payload),
                            cancellationToken: stoppingToken);
                    }

                    await _channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to reply to inbox discovery ping");
                    try { await _channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: stoppingToken); }
                    catch (Exception ackEx) { _logger.LogDebug(ackEx, "Failed to acknowledge discovery message"); }
                }
            };

            await _channel.BasicConsumeAsync(queue: queue, autoAck: false, consumer: consumer, cancellationToken: stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // shutdown requested
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis inbox announce loop failed");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        try { if (_channel is not null) await _channel.CloseAsync(cancellationToken: cancellationToken); }
        catch (Exception ex) { _logger.LogDebug(ex, "Error closing RabbitMQ channel"); }

        try { _channel?.Dispose(); }
        catch (Exception ex) { _logger.LogDebug(ex, "Error disposing RabbitMQ channel"); }

        try { if (_connection is not null) await _connection.CloseAsync(cancellationToken); }
        catch (Exception ex) { _logger.LogDebug(ex, "Error closing RabbitMQ connection"); }

        try { _connection?.Dispose(); }
        catch (Exception ex) { _logger.LogDebug(ex, "Error disposing RabbitMQ connection"); }

        await base.StopAsync(cancellationToken);
    }

    private string? ResolveRabbitConnectionString()
    {
        return _configuration["Koan:Messaging:Buses:default:ConnectionString"]
            ?? _configuration["Koan:Messaging:Buses:rabbit:ConnectionString"]
            ?? _configuration["ConnectionStrings:RabbitMq"];
    }

    private string ResolveExchange()
        => _configuration["Koan:Messaging:Buses:default:RabbitMq:Exchange"]
            ?? _configuration["Koan:Messaging:Buses:rabbit:RabbitMq:Exchange"]
            ?? "Koan";

    private string ResolveEndpoint()
    {
        var explicitEndpoint = _configuration["Koan:Messaging:Inbox:Endpoint"];
        if (!string.IsNullOrWhiteSpace(explicitEndpoint)) return explicitEndpoint!;

        var host = Environment.GetEnvironmentVariable("HOSTNAME") ?? "localhost";
        var port = Environment.GetEnvironmentVariable("PORT") ?? InferPort();
        return $"http://{host}:{port}";
    }

    private string InferPort()
    {
        var urls = _configuration["ASPNETCORE_URLS"];
        if (!string.IsNullOrWhiteSpace(urls))
        {
            foreach (var raw in urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (Uri.TryCreate(raw, UriKind.Absolute, out var uri) && uri.Port > 0)
                {
                    return uri.Port.ToString();
                }
            }
        }

        return _environment.IsDevelopment() ? "8080" : "80";
    }
}