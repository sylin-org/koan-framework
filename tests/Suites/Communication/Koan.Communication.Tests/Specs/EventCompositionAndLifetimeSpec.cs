using Koan.Communication.Tests.Support;
using Koan.Core.Diagnostics;
using static Koan.Communication.Tests.Support.EventHandlerFixtures;
using static Koan.Communication.Tests.Support.TransportReceiverFixtures;

namespace Koan.Communication.Tests.Specs;

public sealed class EventCompositionAndLifetimeSpec
{
    [Fact]
    public async Task Foundation_AddKoan_composes_local_events_and_reports_their_decisions()
    {
        var ct = TestContext.Current.CancellationToken;
        var state = new EventTestState();
        await using var host = await CommunicationTestHost.Start(state, ct);

        var facts = host.Services.GetRequiredService<IKoanRuntimeFacts>().Current;
        facts.Complete.Should().BeTrue();
        facts.Facts.Should().Contain(fact =>
            fact.Code == "koan.communication.events.selected"
            && fact.Subject == "communication:events:default"
            && fact.ReasonCode == "built-in-floor");
        facts.Facts.Should().Contain(fact =>
            fact.Code == "koan.communication.events.subscriptions.discovered"
            && fact.Summary.Contains("Zero subscriptions", StringComparison.Ordinal));
        facts.Facts.Should().Contain(fact =>
            fact.Code == "koan.communication.events.subscription.discovered"
            && fact.Summary.Contains(nameof(CopyDetails), StringComparison.Ordinal)
            && fact.Summary.Contains("requires explicit details", StringComparison.Ordinal));
        facts.Facts.Should().Contain(fact =>
            fact.Code == "koan.communication.events.bounds"
            && fact.Subject == "communication:events:bounds"
            && fact.Summary.Contains("process-memory", StringComparison.Ordinal)
            && fact.Summary.Contains("256-occurrence", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Graceful_host_stop_drains_an_accepted_occurrence()
    {
        var ct = TestContext.Current.CancellationToken;
        var state = new EventTestState();
        await using var host = await CommunicationTestHost.Start(state, ct);
        using var hostScope = AppHost.PushScope(host.Services);

        var acceptance = await new DrainEventOrder().Events.Raise<DrainEvent>(ct);
        await state.DrainStarted.Task.WaitAsync(ct);
        var stop = host.StopAsync(ct);
        var early = await Task.WhenAny(stop, Task.Delay(TimeSpan.FromMilliseconds(100), ct));
        early.Should().NotBe(stop);

        state.DrainRelease.TrySetResult(true);
        await stop;
        var settlement = await acceptance.WaitForSettlement(ct);

        state.DrainHandled.Should().Be(1);
        settlement.Delivered.Should().Be(1);
    }

    [Fact]
    public async Task Repeated_hosts_keep_event_dependencies_and_delivery_counts_isolated()
    {
        var ct = TestContext.Current.CancellationToken;
        var first = new EventTestState();
        await using (var host = await CommunicationTestHost.Start(first, ct))
        {
            using var hostScope = AppHost.PushScope(host.Services);
            var acceptance = await new IsolationEventOrder().Events.Raise<IsolationEvent>(ct);
            await acceptance.WaitForSettlement(ct);
        }

        var second = new EventTestState();
        await using (var host = await CommunicationTestHost.Start(second, ct))
        {
            using var hostScope = AppHost.PushScope(host.Services);
            var acceptance = await new IsolationEventOrder().Events.Raise<IsolationEvent>(ct);
            await acceptance.WaitForSettlement(ct);
        }

        first.IsolationHandled.Should().Be(1);
        second.IsolationHandled.Should().Be(1);
    }

    [Fact]
    public async Task Event_and_transport_lanes_do_not_block_each_others_local_delivery()
    {
        var ct = TestContext.Current.CancellationToken;
        var events = new EventTestState();
        var transport = new TransportTestState();
        await using var host = await CommunicationTestHost.Start(
            events,
            ct,
            services => services.AddSingleton(transport));
        using var hostScope = AppHost.PushScope(host.Services);

        var eventAcceptance = await new BlockingEventOrder().Events.Raise<OrderApproved>(ct);
        await events.BlockingStarted.Task.WaitAsync(ct);

        var transportAcceptance = await new IsolationOrder().Transport.Send(ct);
        var transportSettlement = await transportAcceptance.WaitForSettlement(ct);
        transportSettlement.Delivered.Should().Be(1);
        transport.IsolationHandled.Should().Be(1);

        events.BlockingRelease.TrySetResult(true);
        (await eventAcceptance.WaitForSettlement(ct)).Delivered.Should().Be(1);
    }
}
