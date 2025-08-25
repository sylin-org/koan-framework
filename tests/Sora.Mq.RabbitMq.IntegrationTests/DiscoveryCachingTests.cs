using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Sora.Core;
using Sora.Data.Core;
using Sora.Messaging;
using Sora.Testing;
using System.Text.Json;
using Xunit;

namespace Sora.Mq.RabbitMq.IntegrationTests;

public class DiscoveryCachingTests : IAsyncLifetime
{
    private TestcontainersContainer? _rabbit;
    private int _hostPort = 5677;
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

    [SkippableFact]
    public async Task Discovery_result_is_cached_for_subsequent_calls()
    {
        Skip.IfNot(_available, "Docker is not running or misconfigured; skipping RabbitMQ discovery caching test.");
        var conn = $"amqp://guest:guest@localhost:{_hostPort}";
        var exchange = "sora-test";
        var group = "workers"; var busCode = "rabbit";

        // Announcer
        var factory = new ConnectionFactory { Uri = new Uri(conn), DispatchConsumersAsync = true };
        using var connection = factory.CreateConnection("disc-cache");
        using var channel = connection.CreateModel();
        channel.ExchangeDeclare(exchange, type: "topic", durable: true);
        var q = channel.QueueDeclare(string.Empty, false, true, true).QueueName;
        var rk = $"sora.discovery.ping.{busCode}.{group}";
        channel.QueueBind(q, exchange, rk);
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.Received += async (s, ea) =>
        {
            var replyTo = ea.BasicProperties?.ReplyTo; var corr = ea.BasicProperties?.CorrelationId;
            if (!string.IsNullOrWhiteSpace(replyTo))
            {
                var body = JsonSerializer.SerializeToUtf8Bytes(new { endpoint = "http://localhost:19091" });
                var props = channel.CreateBasicProperties(); props.CorrelationId = corr; props.ContentType = "application/json";
                channel.BasicPublish(exchange, replyTo!, false, props, body);
            }
            channel.BasicAck(ea.DeliveryTag, false);
            await Task.CompletedTask;
        };
        channel.BasicConsume(q, false, consumer);

        // App configured with short timeout and longer cache
        var services = new ServiceCollection();
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sora:Messaging:DefaultBus"] = busCode,
                ["Sora:Messaging:DefaultGroup"] = group,
                ["Sora:Messaging:Discovery:Enabled"] = "true",
                ["Sora:Messaging:Discovery:CacheMinutes"] = "10",
                ["Sora:Messaging:Buses:rabbit:Provider"] = "RabbitMq",
                ["Sora:Messaging:Buses:rabbit:ConnectionString"] = conn,
                ["Sora:Messaging:Buses:rabbit:RabbitMq:Exchange"] = exchange
            })
            .Build();
        services.AddSingleton<IConfiguration>(cfg);
        services.AddSora();
        var sp = services.BuildServiceProvider();
        sp.UseSora();

        // First discovery: triggers ping
        var client = sp.GetRequiredService<IInboxDiscoveryClient>();
        var ep1 = await client.DiscoverAsync();
        ep1.Should().Be("http://localhost:19091");

        // Shut down announcer (simulate unavailability)
        channel.QueueUnbind(q, exchange, rk);

        // Second discovery should return cached endpoint, not null
        var ep2 = await client.DiscoverAsync();
        ep2.Should().Be(ep1);
    }
}