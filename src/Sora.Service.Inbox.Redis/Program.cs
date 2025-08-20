using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using StackExchange.Redis;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

var cfg = builder.Configuration;
var redisConn = cfg["Sora:Inbox:Redis:ConnectionString"] ?? cfg[$"ConnectionStrings:InboxRedis"] ?? "localhost:6379";
var mux = await ConnectionMultiplexer.ConnectAsync(redisConn);
builder.Services.AddSingleton<IConnectionMultiplexer>(mux);

builder.Services.AddControllers();
var app = builder.Build();

// Build base URL for announce (prefer explicit config, else infer from environment)
string ResolveEndpoint()
{
    var ep = cfg["Sora:Messaging:Inbox:Endpoint"];
    if (!string.IsNullOrWhiteSpace(ep)) return ep!;
    var port = Environment.GetEnvironmentVariable("PORT") ?? cfg["ASPNETCORE_URLS"]?.Split(';').Select(u => new Uri(u)).FirstOrDefault()?.Port.ToString();
    var p = string.IsNullOrWhiteSpace(port) ? "8080" : port;
    var host = Environment.GetEnvironmentVariable("HOSTNAME") ?? "localhost";
    return $"http://{host}:{p}";
}

// Optional RabbitMQ announce: reply to discovery pings with our endpoint
void TryStartAnnouncer()
{
    try
    {
        var connStr = cfg["Sora:Messaging:Buses:default:ConnectionString"]
                   ?? cfg["Sora:Messaging:Buses:rabbit:ConnectionString"]
                   ?? cfg["ConnectionStrings:RabbitMq"];
        if (string.IsNullOrWhiteSpace(connStr)) return; // no MQ configured

        var exchange = cfg["Sora:Messaging:Buses:default:RabbitMq:Exchange"]
                    ?? cfg["Sora:Messaging:Buses:rabbit:RabbitMq:Exchange"]
                    ?? "sora";
        var group = cfg["Sora:Messaging:DefaultGroup"] ?? "workers";
        var busCode = cfg["Sora:Messaging:DefaultBus"] ?? "rabbit";

        var factory = new ConnectionFactory { Uri = new Uri(connStr!), DispatchConsumersAsync = true, AutomaticRecoveryEnabled = true, TopologyRecoveryEnabled = true };
        var connection = factory.CreateConnection("sora-inbox-redis");
        var channel = connection.CreateModel();
        channel.ExchangeDeclare(exchange: exchange, type: "topic", durable: true, autoDelete: false, arguments: null);
        var queue = channel.QueueDeclare(queue: string.Empty, durable: false, exclusive: true, autoDelete: true, arguments: null).QueueName;
        var rk = $"sora.discovery.ping.{busCode}.{group}";
        channel.QueueBind(queue: queue, exchange: exchange, routingKey: rk);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.Received += async (sender, ea) =>
        {
            try
            {
                var replyTo = ea.BasicProperties?.ReplyTo;
                var corr = ea.BasicProperties?.CorrelationId;
                if (!string.IsNullOrWhiteSpace(replyTo))
                {
                    var payload = JsonSerializer.SerializeToUtf8Bytes(new { endpoint = ResolveEndpoint(), kind = "inbox", name = "redis", version = "v1" });
                    var props = channel.CreateBasicProperties();
                    props.CorrelationId = corr;
                    props.ContentType = "application/json";
                    channel.BasicPublish(exchange: exchange, routingKey: replyTo!, mandatory: false, basicProperties: props, body: payload);
                }
                channel.BasicAck(ea.DeliveryTag, false);
            }
            catch { try { channel.BasicAck(ea.DeliveryTag, false); } catch { } }
            await Task.CompletedTask;
        };
        channel.BasicConsume(queue: queue, autoAck: false, consumer: consumer);

        app.Lifetime.ApplicationStopping.Register(() =>
        {
            try { channel.Close(); } catch { }
            try { channel.Dispose(); } catch { }
            try { connection.Close(); } catch { }
            try { connection.Dispose(); } catch { }
        });
    }
    catch { /* swallow in dev */ }
}

TryStartAnnouncer();

app.MapControllers();

app.Run();
