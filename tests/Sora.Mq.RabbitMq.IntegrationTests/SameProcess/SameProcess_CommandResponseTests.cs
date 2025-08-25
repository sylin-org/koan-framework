using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Sora.Messaging;
using Sora.Mq.RabbitMq.IntegrationTests.Fixtures;
using Xunit;

namespace Sora.Mq.RabbitMq.IntegrationTests.SameProcess;

[Collection(RabbitMqCollection.Name)]
public sealed class SameProcess_CommandResponseTests
{
    private readonly RabbitMqSharedContainer _rmq;
    public SameProcess_CommandResponseTests(RabbitMqSharedContainer rmq) => _rmq = rmq;

    [SkippableFact]
    public async Task Command_response_roundtrip_in_same_process()
    {
        Skip.IfNot(_rmq.Available, "Docker is not running or misconfigured; skipping RabbitMQ container-based test.");
        var exchange = $"sora-test-{Guid.NewGuid():N}";
        var correlation = Guid.NewGuid().ToString("N");

        var responses = new List<UserCreated>();
        await using var host = MessagingHost.Start(
            _rmq.ConnectionString,
            exchange,
            services =>
            {
                services.On<CreateUser>(async (cmd, ct) =>
                {
                    // Correlation is automatically propagated from AMQP properties if set by publisher; here we just publish the event
                    await new UserCreated { UserId = cmd.UserId }.Send();
                });
                services.On<UserCreated>((evt, ct) =>
                {
                    responses.Add(evt);
                    return Task.CompletedTask;
                });
            });

        await new CreateUser { UserId = "u-1" }.Send();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (responses.Count == 0 && sw.Elapsed < TimeSpan.FromSeconds(5))
            await Task.Delay(50);

        responses.Should().HaveCount(1);
        responses[0].UserId.Should().Be("u-1");
    }

    [Message(Alias = "Users.Create")]
    public sealed class CreateUser { public string UserId { get; init; } = string.Empty; }

    [Message(Alias = "Users.Created")]
    public sealed class UserCreated { public string UserId { get; init; } = string.Empty; }
}
