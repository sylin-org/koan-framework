using Koan.Communication.Tests.Support;

namespace Koan.Communication.Tests.Specs;

public sealed class ProviderElectionSpec
{
    [Fact]
    public async Task Available_but_unintended_external_provider_does_not_replace_the_local_floor()
    {
        var ct = TestContext.Current.CancellationToken;
        var fake = new FakeExternalAdapter(failStart: false);
        var state = new TransportTestState();
        await using var host = await CommunicationTestHost.Start(
            state,
            ct,
            services => services.AddSingleton<ICommunicationAdapter>(fake));
        using var hostScope = AppHost.PushScope(host.Services);

        var acceptance = await new TransportReceiverFixtures.IsolationOrder().Transport.Send(ct);
        await acceptance.WaitForSettlement(ct);

        acceptance.Adapter.Should().Be("in-process");
        fake.StartCount.Should().Be(0);
    }

    [Fact]
    public async Task Unavailable_explicit_provider_fails_startup_without_local_fallback()
    {
        var ct = TestContext.Current.CancellationToken;
        var fake = new FakeExternalAdapter(failStart: true);
        var state = new TransportTestState();
        var start = () => CommunicationTestHost.Start(
            state,
            ct,
            services =>
            {
                services.AddSingleton<ICommunicationAdapter>(fake);
                services.Configure<CommunicationOptions>(options => options.TransportProvider = "fake-external");
            });

        var failure = (await start.Should().ThrowAsync<InvalidOperationException>()).Which;

        failure.Message.Should().Contain("never falls back");
        fake.StartCount.Should().Be(1);
    }

    [Fact]
    public async Task External_transport_cannot_accept_a_known_zero_receiver_route()
    {
        var ct = TestContext.Current.CancellationToken;
        var fake = new FakeExternalAdapter(failStart: false, targetGroups: 0);
        var state = new TransportTestState();
        await using var host = await CommunicationTestHost.Start(
            state,
            ct,
            services =>
            {
                services.AddSingleton<ICommunicationAdapter>(fake);
                services.Configure<CommunicationOptions>(options => options.TransportProvider = "fake-external");
            });
        using var hostScope = AppHost.PushScope(host.Services);

        var send = () => new TransportReceiverFixtures.IsolationOrder().Transport.Send(ct);
        var failure = (await send.Should().ThrowAsync<TransportException>()).Which;

        failure.Failure.Should().Be(TransportException.FailureKind.NoReceivers);
        failure.Acceptance.Adapter.Should().Be("fake-external");
        fake.PublishCount.Should().Be(1);
    }

    [Fact]
    public async Task Named_channel_elects_its_provider_and_scopes_that_adapters_bindings()
    {
        var ct = TestContext.Current.CancellationToken;
        var fake = new FakeExternalAdapter(failStart: false, targetGroups: 1);
        var state = new TransportTestState();
        await using var host = await CommunicationTestHost.Start(
            state,
            ct,
            services =>
            {
                services.AddSingleton<ICommunicationAdapter>(fake);
                services.Configure<CommunicationOptions>(options =>
                {
                    options.TransportProvider = "in-process";
                    options.Channels["external"] = new CommunicationChannelOptions
                    {
                        TransportProvider = "fake-external"
                    };
                });
            });
        using var hostScope = AppHost.PushScope(host.Services);

        var local = await new TransportReceiverFixtures.IsolationOrder().Transport.Send(ct);
        await local.WaitForSettlement(ct);
        var external = await new TransportReceiverFixtures.IsolationOrder()
            .Transport.Send(ct, channel: "external");

        local.Adapter.Should().Be("in-process");
        external.Adapter.Should().Be("fake-external");
        fake.PublishChannels.Should().Equal("external");
        fake.Bindings.Should().OnlyContain(binding =>
            binding.Lane != CommunicationLane.Transport || binding.Channel == "external");
        fake.Bindings.Should().Contain(binding =>
            binding.Lane == CommunicationLane.Transport && binding.Channel == "external");
    }

    private sealed class FakeExternalAdapter(bool failStart, int? targetGroups = null) : ICommunicationAdapter
    {
        public CommunicationAdapterDescriptor Descriptor { get; } = new(
            "fake-external",
            [CommunicationLane.Transport],
            CommunicationDeliveryAssurance.Acknowledged,
            CommunicationAdapterCapabilities.ContractIdentity
            | CommunicationAdapterCapabilities.SnapshotCopy
            | CommunicationAdapterCapabilities.ContextCarriage
            | CommunicationAdapterCapabilities.TypedGroups
            | CommunicationAdapterCapabilities.GroupFanOut
            | CommunicationAdapterCapabilities.MessageIdentity
            | CommunicationAdapterCapabilities.BoundedAcceptance,
            ["Sylin.Koan.Communication.Connector.Fake"]);

        public int StartCount { get; private set; }
        public int PublishCount { get; private set; }
        public IReadOnlyList<CommunicationAdapterBinding> Bindings { get; private set; } = [];
        public List<string> PublishChannels { get; } = [];
        public bool IsReady { get; private set; }

        public Task Start(CommunicationAdapterHost host, CancellationToken ct)
        {
            StartCount++;
            if (failStart) throw new InvalidOperationException("expected unavailable provider");
            Bindings = host.Bindings;
            IsReady = true;
            return Task.CompletedTask;
        }

        public ValueTask<CommunicationAdapterAcceptance> Publish(
            CommunicationAdapterPublication publication,
            CancellationToken ct)
        {
            PublishCount++;
            PublishChannels.Add(publication.Channel);
            return ValueTask.FromResult(new CommunicationAdapterAcceptance(targetGroups, false));
        }

        public Task Stop(CancellationToken ct)
        {
            IsReady = false;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
