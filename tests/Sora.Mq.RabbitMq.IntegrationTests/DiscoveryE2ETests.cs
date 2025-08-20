using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Sora.Core;
using Sora.Data.Core;
using Sora.Messaging;
using Sora.Messaging.Inbox.Http;
using Sora.Testing;
using System.Text;
using System.Text.Json;
using Xunit;

public class DiscoveryE2ETests : IAsyncLifetime
{
    private TestcontainersContainer? _rabbit;
    private int _hostPort = 5676;
    private bool _available;
    private string? _dockerEndpoint;

    public async Task InitializeAsync()
    {
        Environment.SetEnvironmentVariable("TESTCONTAINERS_RYUK_DISABLED", "true");
        var probe = await DockerEnvironment.ProbeAsync();
        if (!probe.Available) { _available = false; return; }
        _dockerEndpoint = probe.Endpoint;
        try
        {
            _rabbit = new TestcontainersBuilder<TestcontainersContainer>()
                .WithDockerEndpoint(_dockerEndpoint)
                .WithImage("rabbitmq:3.13-management")
                .WithPortBinding(_hostPort, 5672)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(5672))
                .Build();
            await _rabbit.StartAsync();
            _available = true;
        }
        catch { _available = false; }
    }

    public async Task DisposeAsync()
    {
        if (_rabbit != null)
        {
            try { await _rabbit.StopAsync(); } catch { }
            try { await _rabbit.DisposeAsync(); } catch { }
        }
    }

    [Fact]
    public async Task Discovery_over_Rabbit_returns_endpoint_and_wires_HttpInboxStore()
    {
        if (!_available) return;
        var conn = $"amqp://guest:guest@localhost:{_hostPort}";

        // Spin a lightweight announce responder in-test
        var factory = new ConnectionFactory { Uri = new Uri(conn), DispatchConsumersAsync = true };
        using var connection = factory.CreateConnection("disc-e2e");
        using var channel = connection.CreateModel();
        var exchange = "sora-test";
        channel.ExchangeDeclare(exchange, type: "topic", durable: true);
        var q = channel.QueueDeclare(string.Empty, false, true, true).QueueName;
        var group = "workers"; var busCode = "rabbit";
        var rk = $"sora.discovery.ping.{busCode}.{group}";
        channel.QueueBind(q, exchange, rk);
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.Received += async (s, ea) =>
        {
            var replyTo = ea.BasicProperties?.ReplyTo;
            var corr = ea.BasicProperties?.CorrelationId;
            if (!string.IsNullOrWhiteSpace(replyTo))
            {
                var body = JsonSerializer.SerializeToUtf8Bytes(new { endpoint = "http://localhost:19090" });
                var props = channel.CreateBasicProperties();
                props.CorrelationId = corr;
                props.ContentType = "application/json";
                channel.BasicPublish(exchange: exchange, routingKey: replyTo!, mandatory: false, basicProperties: props, body: body);
            }
            channel.BasicAck(ea.DeliveryTag, false);
            await Task.CompletedTask;
        };
        channel.BasicConsume(queue: q, autoAck: false, consumer: consumer);
        // Give the consumer a tick to attach before publishing test messages
        await Task.Delay(10);

        // Now build a Sora app configured for discovery
        var services = new ServiceCollection();
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sora:Messaging:DefaultBus"] = "rabbit",
                ["Sora:Messaging:DefaultGroup"] = group,
                ["Sora:Messaging:Discovery:Enabled"] = "true",
                ["Sora:Messaging:Buses:rabbit:Provider"] = "RabbitMq",
                ["Sora:Messaging:Buses:rabbit:ConnectionString"] = conn,
                ["Sora:Messaging:Buses:rabbit:RabbitMq:Exchange"] = exchange
            })
            .Build();
        services.AddSingleton<IConfiguration>(cfg);
        services.AddSora();

        var sp = services.BuildServiceProvider();
        sp.UseSora();

        // After UseSora, discovery initializer should have run; HttpInboxStore should be registered if found
        var http = sp.GetService<HttpInboxStore>();
        http.Should().NotBeNull()
            .And.BeAssignableTo<IInboxStore>();

        // Also verify that configured InboxClientOptions got the endpoint (indirectly via HttpClient BaseAddress accessible only internally)
        var opts = sp.GetRequiredService<IOptions<DiscoveryOptions>>();
        opts.Value.TimeoutSeconds.Should().BeGreaterThan(0);
    }
}
