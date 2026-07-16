using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Koan.Cache.Abstractions.Coherence;
using Koan.Cache.Abstractions.Primitives;
using Koan.Cache.Coherence.Messaging.Channel;
using Koan.Core;
using Koan.Data.Abstractions;
using Koan.Messaging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Koan.Tests.Cache.Coherence.Messaging.Specs;

public sealed class MessagingCoherenceChannelSpec
{
    private sealed class CapturingProxy : IMessageProxy
    {
        public List<object> Sent { get; } = new();
        public bool IsLive => true;
        public int BufferedMessageCount => 0;
        public Task SendAsync<T>(T message, CancellationToken ct = default) where T : class
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingProxy : IMessageProxy
    {
        public bool IsLive => true;
        public int BufferedMessageCount => 0;
        public Task SendAsync<T>(T message, CancellationToken ct = default) where T : class
            => Task.FromException(new InvalidOperationException("transport down"));
    }

    [Fact]
    public void Declares_ProviderPriority_150()
    {
        var attr = typeof(MessagingCoherenceChannel).GetCustomAttribute<ProviderPriorityAttribute>();

        attr.Should().NotBeNull();
        attr!.Priority.Should().Be(150, "outranks Redis pub/sub at 100; rationale = reuse existing messaging infra");
    }

    [Fact]
    public void TransportName_is_koan_messaging()
    {
        var channel = new MessagingCoherenceChannel(new CapturingProxy(), NullLogger<MessagingCoherenceChannel>.Instance);
        channel.TransportName.Should().Be("koan-messaging");
    }

    [Fact]
    public void Capabilities_are_BestEffort()
    {
        var channel = new MessagingCoherenceChannel(new CapturingProxy(), NullLogger<MessagingCoherenceChannel>.Instance);
        channel.Capabilities.Should().Be(CoherenceCapabilities.BestEffort);
    }

    [Fact]
    public async Task Publish_routes_envelope_through_proxy()
    {
        var proxy = new CapturingProxy();
        var channel = new MessagingCoherenceChannel(proxy, NullLogger<MessagingCoherenceChannel>.Instance);
        var msg = CacheInvalidation.EvictKey(new CacheKey("Todo:_:abc"), Guid.NewGuid());

        await channel.Publish(msg, CancellationToken.None);

        proxy.Sent.Should().HaveCount(1);
        proxy.Sent[0].Should().BeOfType<MessagingInvalidationEnvelope>();
        ((MessagingInvalidationEnvelope)proxy.Sent[0]).Key.Should().Be("Todo:_:abc");
    }

    [Fact]
    public async Task Publish_swallows_proxy_failures_to_honour_best_effort_contract()
    {
        var channel = new MessagingCoherenceChannel(new ThrowingProxy(), NullLogger<MessagingCoherenceChannel>.Instance);
        var msg = CacheInvalidation.EvictKey(new CacheKey("Todo:_:abc"), Guid.NewGuid());

        var act = async () => await channel.Publish(msg, CancellationToken.None);

        await act.Should().NotThrowAsync("coherence is best-effort; publish must never throw");
    }

    [Fact]
    public async Task CatchUp_is_noop_returns_null_cursor()
    {
        var channel = new MessagingCoherenceChannel(new CapturingProxy(), NullLogger<MessagingCoherenceChannel>.Instance);

        var cursor = await channel.CatchUp("any-previous-cursor", (_, _) => ValueTask.CompletedTask, CancellationToken.None);

        cursor.Should().BeNull();
    }

    [Fact]
    public async Task HandleIncoming_before_Subscribe_is_silently_dropped()
    {
        var channel = new MessagingCoherenceChannel(new CapturingProxy(), NullLogger<MessagingCoherenceChannel>.Instance);
        var envelope = MessagingInvalidationEnvelope.FromMessage(
            CacheInvalidation.EvictKey(new CacheKey("Todo:_:abc"), Guid.NewGuid()));

        var act = async () =>
            await (Task)typeof(MessagingCoherenceChannel)
                .GetMethod("HandleIncoming", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(channel, new object[] { envelope })!;

        await act.Should().NotThrowAsync("messages arriving before Subscribe must drop, not throw");
    }

    [Fact]
    public async Task HandleIncoming_after_Subscribe_dispatches_to_handler()
    {
        var channel = new MessagingCoherenceChannel(new CapturingProxy(), NullLogger<MessagingCoherenceChannel>.Instance);

        CacheInvalidation? received = null;
        await channel.Subscribe((msg, _) =>
        {
            received = msg;
            return ValueTask.CompletedTask;
        }, CancellationToken.None);

        var originalKey = new CacheKey("Todo:_:abc");
        var envelope = MessagingInvalidationEnvelope.FromMessage(
            CacheInvalidation.EvictKey(originalKey, Guid.NewGuid()));

        await (Task)typeof(MessagingCoherenceChannel)
            .GetMethod("HandleIncoming", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(channel, new object[] { envelope })!;

        received.Should().NotBeNull();
        received!.Value.Key.Should().Be(originalKey);
    }

    [Fact]
    public async Task HandleIncoming_swallows_handler_exceptions()
    {
        var channel = new MessagingCoherenceChannel(new CapturingProxy(), NullLogger<MessagingCoherenceChannel>.Instance);

        await channel.Subscribe((_, _) => throw new InvalidOperationException("downstream boom"), CancellationToken.None);

        var envelope = MessagingInvalidationEnvelope.FromMessage(
            CacheInvalidation.EvictKey(new CacheKey("Todo:_:abc"), Guid.NewGuid()));

        var act = async () =>
            await (Task)typeof(MessagingCoherenceChannel)
                .GetMethod("HandleIncoming", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(channel, new object[] { envelope })!;

        await act.Should().NotThrowAsync("coherence receive-side must never throw — it's best-effort by contract");
    }
}
