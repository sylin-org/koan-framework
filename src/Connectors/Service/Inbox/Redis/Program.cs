using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using StackExchange.Redis;
using Newtonsoft.Json;

var builder = WebApplication.CreateBuilder(args);

var cfg = builder.Configuration;
var redisConn = cfg["Koan:Inbox:Redis:ConnectionString"] ?? cfg[$"ConnectionStrings:InboxRedis"] ?? "localhost:6379";
var mux = await ConnectionMultiplexer.ConnectAsync(redisConn);
builder.Services.AddSingleton<IConnectionMultiplexer>(mux);

builder.Services.AddControllers();
var app = builder.Build();

// Build base URL for announce (prefer explicit config, else infer from environment)
string ResolveEndpoint()
{
    var ep = cfg["Koan:Messaging:Inbox:Endpoint"];
    if (!string.IsNullOrWhiteSpace(ep)) return ep!;
    var port = Environment.GetEnvironmentVariable("PORT") ?? cfg["ASPNETCORE_URLS"]?.Split(';').Select(u => new Uri(u)).FirstOrDefault()?.Port.ToString();
    var p = string.IsNullOrWhiteSpace(port) ? "8080" : port;
    var host = Environment.GetEnvironmentVariable("HOSTNAME") ?? "localhost";
    return $"http://{host}:{p}";
}

// Optional RabbitMQ announce: reply to discovery pings with our endpoint
async Task TryStartAnnouncerAsync()
{
    try
    {
        var connStr = cfg["Koan:Messaging:Buses:default:ConnectionString"]
                   ?? cfg["Koan:Messaging:Buses:rabbit:ConnectionString"]
                   ?? cfg["ConnectionStrings:RabbitMq"];
        if (string.IsNullOrWhiteSpace(connStr)) return; // no MQ configured

        var exchange = cfg["Koan:Messaging:Buses:default:RabbitMq:Exchange"]
                    ?? cfg["Koan:Messaging:Buses:rabbit:RabbitMq:Exchange"]
                    ?? "Koan";
        var group = cfg["Koan:Messaging:DefaultGroup"] ?? "workers";
        var busCode = cfg["Koan:Messaging:DefaultBus"] ?? "rabbit";

        var factory = new ConnectionFactory { Uri = new Uri(connStr!), AutomaticRecoveryEnabled = true, TopologyRecoveryEnabled = true };
        var connection = await factory.CreateConnectionAsync();
        var channel = await connection.CreateChannelAsync();
        await channel.ExchangeDeclareAsync(exchange: exchange, type: "topic", durable: true, autoDelete: false, arguments: null);
        var queue = (await channel.QueueDeclareAsync(queue: string.Empty, durable: false, exclusive: true, autoDelete: true, arguments: null)).QueueName;
        var rk = $"Koan.discovery.ping.{busCode}.{group}";
        await channel.QueueBindAsync(queue: queue, exchange: exchange, routingKey: rk);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (sender, ea) =>
        {
            try
            {
                var replyTo = ea.BasicProperties?.ReplyTo;
                var corr = ea.BasicProperties?.CorrelationId;
                if (!string.IsNullOrWhiteSpace(replyTo))
                {
                    var payloadJson = JsonConvert.SerializeObject(new { endpoint = ResolveEndpoint(), kind = "inbox", name = "redis", version = "v1" });
                    var payload = System.Text.Encoding.UTF8.GetBytes(payloadJson);
                    var props = new BasicProperties
                    {
                        CorrelationId = corr,
                        ContentType = "application/json"
                    };
                    await channel.BasicPublishAsync(exchange: exchange, routingKey: replyTo!, mandatory: false, basicProperties: props, body: payload);
                }
                await channel.BasicAckAsync(ea.DeliveryTag, false);
            }
            catch { try { await channel.BasicAckAsync(ea.DeliveryTag, false); } catch { } }
        };
        await channel.BasicConsumeAsync(queue: queue, autoAck: false, consumer: consumer);

        app.Lifetime.ApplicationStopping.Register(() =>
        {
            try { channel.Dispose(); } catch { }
            try { connection.Dispose(); } catch { }
        });
    }
    catch { /* swallow in dev */ }
}

await TryStartAnnouncerAsync();

app.MapControllers();

app.Run();
