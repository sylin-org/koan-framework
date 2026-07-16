using Koan.Communication.Signals;
using Koan.Communication.Tests.Support;

namespace Koan.Communication.Tests.Specs;

public sealed class FrameworkBroadcastSpec
{
    [Fact]
    public async Task Process_local_floor_delivers_a_framework_broadcast_to_the_local_node()
    {
        var ct = TestContext.Current.CancellationToken;
        var state = new BroadcastState();
        await using var host = await CommunicationTestHost.Start(
            state,
            ct,
            services =>
            {
                services.AddSingleton<BroadcastHandler>();
                services.AddFrameworkBroadcast<ProbeBroadcast, BroadcastHandler>();
            });

        var signals = host.Services.GetRequiredService<IFrameworkSignalPublisher>();
        signals.BroadcastProviderId.Should().Be("in-process");
        signals.TryBroadcast(new ProbeBroadcast("hello")).Should().BeTrue();

        (await state.Received.Task.WaitAsync(ct)).Should().Be("hello");
    }

    [Fact]
    public async Task Provider_without_node_fanout_cannot_claim_framework_broadcasts()
    {
        var ct = TestContext.Current.CancellationToken;
        var state = new BroadcastState();
        var start = () => CommunicationTestHost.Start(
            state,
            ct,
            services =>
            {
                services.AddSingleton<BroadcastHandler>();
                services.AddFrameworkBroadcast<ProbeBroadcast, BroadcastHandler>();
                services.AddSingleton<ICommunicationAdapter>(new MissingNodeFanOutAdapter());
                services.Configure<CommunicationOptions>(options =>
                    options.FrameworkBroadcastsProvider = "missing-node-fanout");
            });

        var failure = (await start.Should().ThrowAsync<InvalidOperationException>()).Which;
        failure.Message.Should().Contain(nameof(CommunicationAdapterCapabilities.NodeFanOut));
    }

    internal readonly record struct ProbeBroadcast(string Value) : IFrameworkBroadcast<ProbeBroadcast>
    {
        public static string ContractId => "koan.tests.framework-broadcast@1";
    }

    internal sealed class BroadcastHandler(BroadcastState state) : IHandleFrameworkBroadcast<ProbeBroadcast>
    {
        public ValueTask Handle(ProbeBroadcast signal, CancellationToken ct)
        {
            state.Received.TrySetResult(signal.Value);
            return ValueTask.CompletedTask;
        }
    }

    internal sealed class BroadcastState
    {
        public TaskCompletionSource<string> Received { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class MissingNodeFanOutAdapter : ICommunicationAdapter
    {
        public CommunicationAdapterDescriptor Descriptor { get; } = new(
            "missing-node-fanout",
            [CommunicationLane.FrameworkBroadcasts],
            CommunicationDeliveryAssurance.Acknowledged,
            CommunicationAdapterCapabilities.ContractIdentity
            | CommunicationAdapterCapabilities.SnapshotCopy
            | CommunicationAdapterCapabilities.MessageIdentity
            | CommunicationAdapterCapabilities.BoundedAcceptance,
            []);

        public bool IsReady => false;
        public Task Start(CommunicationAdapterHost host, CancellationToken ct) => Task.CompletedTask;
        public ValueTask<CommunicationAdapterAcceptance> Publish(CommunicationAdapterPublication publication, CancellationToken ct)
            => ValueTask.FromResult(new CommunicationAdapterAcceptance(null, false));
        public Task Stop(CancellationToken ct) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
