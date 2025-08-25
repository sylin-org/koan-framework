using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Sora.Messaging;
using Sora.Mq.RabbitMq.IntegrationTests.Fixtures;
using Xunit;

namespace Sora.Mq.RabbitMq.IntegrationTests.Compose_OneToOne;

[Collection(RabbitMqCollection.Name)]
public sealed class Compose_OneToOne_CommandResponseTests
{
    private readonly RabbitMqSharedContainer _rmq;
    public Compose_OneToOne_CommandResponseTests(RabbitMqSharedContainer rmq) => _rmq = rmq;

    [SkippableFact]
    public async Task Single_worker_handles_and_client_receives_response()
    {
        Skip.IfNot(_rmq.Available, "Docker is not running or misconfigured; skipping RabbitMQ container-based test.");
        var exchange = $"sora-11-{Guid.NewGuid():N}";
        var correlation = Guid.NewGuid().ToString("N");
        var group = "workers";

        // Worker host
        await using var worker = MessagingHost.Start(
            _rmq.ConnectionString,
            exchange,
            services =>
            {
                services.On<CreateOrder>(async (cmd, ct) =>
                {
                    await new OrderCreated { OrderId = cmd.OrderId }.Send();
                });
            },
            group: group);

        // Client host
        var responses = new List<OrderCreated>();
        await using var client = MessagingHost.Start(
            _rmq.ConnectionString,
            exchange,
            services =>
            {
                services.On<OrderCreated>((evt, ct) =>
                {
                    responses.Add(evt);
                    return Task.CompletedTask;
                });
            },
            group: "client");

    await new CreateOrder { OrderId = "o-1" }.Send();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (responses.Count == 0 && sw.Elapsed < TimeSpan.FromSeconds(5))
            await Task.Delay(50);

        responses.Should().HaveCount(1);
        responses[0].OrderId.Should().Be("o-1");
    }

    [Message(Alias = "Orders.Create")]
    public sealed class CreateOrder { public string OrderId { get; init; } = string.Empty; }

    [Message(Alias = "Orders.Created")]
    public sealed class OrderCreated { public string OrderId { get; init; } = string.Empty; }
}
