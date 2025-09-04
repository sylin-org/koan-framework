using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Sora.Messaging;
using Sora.Mq.RabbitMq.IntegrationTests.Fixtures;
using Xunit;

namespace Sora.Mq.RabbitMq.IntegrationTests.Compose_OneToMany_RoundRobin;

[Collection(RabbitMqCollection.Name)]
public sealed class Compose_OneToMany_RoundRobinTests
{
    private readonly RabbitMqSharedContainer _rmq;
    public Compose_OneToMany_RoundRobinTests(RabbitMqSharedContainer rmq) => _rmq = rmq;

    [SkippableFact]
    public async Task Commands_are_distributed_across_workers_round_robin()
    {
        Skip.IfNot(_rmq.Available, "Docker is not running or misconfigured; skipping RabbitMQ container-based test.");
        var exchange = $"sora-rr-{Guid.NewGuid():N}";
        var group = "workers";
        var n = 3; // workers
        var m = 60; // messages

        var counts = new int[n];
        var hosts = new List<MessagingHost>();
        for (int i = 0; i < n; i++)
        {
            var idx = i;
            hosts.Add(MessagingHost.Start(
                _rmq.ConnectionString,
                exchange,
                services =>
                {
                    services.On<Work>((cmd, ct) =>
                    {
                        Interlocked.Increment(ref counts[idx]);
                        return Task.CompletedTask;
                    });
                },
                group: group));
        }

        // publisher host
        await using var publisher = MessagingHost.Start(_rmq.ConnectionString, exchange, _ => { }, group: group);
        for (int i = 0; i < m; i++)
            await new Work { JobId = $"j{i}" }.Send();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (counts.Sum() < m && sw.Elapsed < TimeSpan.FromSeconds(10))
            await Task.Delay(50);

        counts.Sum().Should().Be(m);
        var avg = m / (double)n;
        counts.Min().Should().BeGreaterThan((int)(avg * 0.3));
        counts.Max().Should().BeLessOrEqualTo((int)(avg * 1.7));

        foreach (var h in hosts) await h.DisposeAsync();
    }

    // Removed Message attribute - new messaging system doesn't use attributes
    public sealed class Work { public string JobId { get; init; } = string.Empty; }
}
