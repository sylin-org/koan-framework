using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Sora.Messaging;
using Sora.Mq.RabbitMq.IntegrationTests.Fixtures;
using System.Collections.Concurrent;
using Xunit;

namespace Sora.Mq.RabbitMq.IntegrationTests.Compose_OneToMany_Broadcast;

[Collection(RabbitMqCollection.Name)]
public sealed class Compose_OneToMany_BroadcastTests
{
    private readonly RabbitMqSharedContainer _rmq;
    public Compose_OneToMany_BroadcastTests(RabbitMqSharedContainer rmq) => _rmq = rmq;

    [SkippableFact]
    public async Task Broadcast_delivers_to_all_groups()
    {
        Skip.IfNot(_rmq.Available, "Docker is not running or misconfigured; skipping RabbitMQ container-based test.");
        var exchange = $"sora-bc-{Guid.NewGuid():N}";
        var groups = new[] { "billing", "analytics", "ops" };
        var responses = new ConcurrentBag<string>();

        var hosts = new List<MessagingHost>();
        foreach (var g in groups)
        {
            hosts.Add(MessagingHost.Start(
                _rmq.ConnectionString,
                exchange,
                services =>
                {
                    services.On<UserRegistered>((evt, ct) =>
                    {
                        responses.Add(g);
                        return Task.CompletedTask;
                    });
                },
                group: g,
                extra: ($"Sora:Messaging:Buses:rabbit:Subscriptions:0:RoutingKeys:0", "Users.Registered")));
        }

        // publisher host
        await using var publisher = MessagingHost.Start(_rmq.ConnectionString, exchange, _ => { }, group: "publisher");
        await new UserRegistered { UserId = "u-1" }.Send();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (responses.Count < groups.Length && sw.Elapsed < TimeSpan.FromSeconds(5))
            await Task.Delay(50);

        responses.Should().HaveCount(groups.Length);
        responses.Should().BeEquivalentTo(groups);

        foreach (var h in hosts) await h.DisposeAsync();
    }

    // Removed Message attribute - new messaging system doesn't use attributes
    public sealed class UserRegistered { public string UserId { get; init; } = string.Empty; }
}
