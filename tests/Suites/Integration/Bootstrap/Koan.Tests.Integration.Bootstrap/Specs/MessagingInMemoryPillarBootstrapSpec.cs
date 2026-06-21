using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Core;
using Koan.Messaging;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Tests.Integration.Bootstrap.Specs;

/// <summary>
/// Boot-smoke for the in-process messaging floor (per ARCH-0079). Proves the Channels-backed InMemory
/// provider is discovered through real <c>AddKoan()</c> reflective bootstrap (Reference = Intent), occupies
/// the reserved Priority-10 slot, and delivers a real Send → consume round-trip with working pause/resume —
/// the zero-broker messaging the single-binary story needs. This is also the provider whose absence the
/// boot report used to claim falsely (Core's <c>InMemoryProvider.Available=true</c> lie, now removed).
/// </summary>
public sealed class MessagingInMemoryPillarBootstrapSpec
{
    public sealed record Ping(string Text);

    private static async Task<IntegrationHost> BootAsync()
        => await KoanIntegrationHost.Configure()
            .WithSetting("Koan:Environment", "Test")
            .ConfigureServices(services => services.AddKoan())
            .StartAsync();

    [Fact]
    public async Task AddKoan_discovers_inmemory_provider_and_round_trips()
    {
        await using var host = await BootAsync();

        var inmemory = host.Services.GetServices<IMessagingProvider>().SingleOrDefault(p => p.Name == "InMemory");
        inmemory.Should().NotBeNull("the InMemory messaging connector must be discovered via Reference = Intent");
        inmemory!.Priority.Should().Be(10);
        (await inmemory.CanConnect()).Should().BeTrue();

        var bus = await inmemory.CreateBus();
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var consumer = await bus.CreateConsumerAsync<Ping>(p =>
        {
            received.TrySetResult(p.Text);
            return Task.CompletedTask;
        });

        await bus.SendAsync(new Ping("hello"));

        var got = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        got.Should().Be("hello");
    }

    [Fact]
    public async Task Paused_consumer_buffers_then_drains_on_resume()
    {
        await using var host = await BootAsync();
        var provider = host.Services.GetServices<IMessagingProvider>().Single(p => p.Name == "InMemory");
        var bus = await provider.CreateBus();

        var count = 0;
        var bothSeen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var consumer = await bus.CreateConsumerAsync<Ping>(_ =>
        {
            if (Interlocked.Increment(ref count) == 2) bothSeen.TrySetResult();
            return Task.CompletedTask;
        });

        await consumer.Pause();
        consumer.IsActive.Should().BeFalse();

        await bus.SendAsync(new Ping("a"));
        await bus.SendAsync(new Ping("b"));
        await Task.Delay(150);
        Volatile.Read(ref count).Should().Be(0, "a paused consumer must buffer, not process");

        await consumer.Resume();
        consumer.IsActive.Should().BeTrue();

        await bothSeen.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Volatile.Read(ref count).Should().Be(2);
    }
}
