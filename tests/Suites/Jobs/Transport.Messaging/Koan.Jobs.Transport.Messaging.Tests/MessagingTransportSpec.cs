using System.Diagnostics;
using AwesomeAssertions;
using Koan.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Koan.Jobs.Transport.Messaging.Tests;

/// <summary>The messaging transport: a submit wakes the local node and fans a ready-signal to peers; an inbound
/// peer signal wakes this node, but the node ignores its own echo.</summary>
public sealed class MessagingTransportSpec
{
    [Fact]
    public async Task notify_wakes_locally_and_fans_out_then_ignores_its_own_echo()
    {
        var proxy = new CapturingProxy();
        var t = new MessagingJobTransport(proxy, NullLogger<MessagingJobTransport>.Instance);

        t.Notify();

        // Local wake — WaitForWork returns immediately.
        var sw = Stopwatch.StartNew();
        await t.WaitForWork(TimeSpan.FromSeconds(5), default);
        sw.ElapsedMilliseconds.Should().BeLessThan(1000);

        // Fanned a ready-signal out to peers.
        await proxy.WaitForOne();
        var own = proxy.Sent.Should().ContainSingle().Subject;

        // Its own echo is ignored — no local wake, so the next wait times out.
        t.OnRemote(own);
        sw.Restart();
        await t.WaitForWork(TimeSpan.FromMilliseconds(60), default);
        sw.ElapsedMilliseconds.Should().BeGreaterThanOrEqualTo(50);

        // A peer's signal does wake this node.
        t.OnRemote(new JobReadySignal { OriginNode = "peer-node" });
        sw.Restart();
        await t.WaitForWork(TimeSpan.FromSeconds(5), default);
        sw.ElapsedMilliseconds.Should().BeLessThan(1000);
    }

    private sealed class CapturingProxy : IMessageProxy
    {
        private readonly TaskCompletionSource _one = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<JobReadySignal> Sent { get; } = new();
        public bool IsLive => true;
        public int BufferedMessageCount => 0;

        public Task SendAsync<T>(T message, CancellationToken cancellationToken = default) where T : class
        {
            if (message is JobReadySignal s) { lock (Sent) Sent.Add(s); _one.TrySetResult(); }
            return Task.CompletedTask;
        }

        public Task WaitForOne() => _one.Task;
    }
}
