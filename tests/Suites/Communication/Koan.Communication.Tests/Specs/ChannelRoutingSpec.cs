using Koan.Communication.Tests.Support;
using static Koan.Communication.Tests.Support.EventHandlerFixtures;
using static Koan.Communication.Tests.Support.TransportReceiverFixtures;

namespace Koan.Communication.Tests.Specs;

public sealed class ChannelRoutingSpec
{
    [Fact]
    public async Task Declared_business_channel_carries_local_transport_and_events_and_reports_the_plan()
    {
        var ct = TestContext.Current.CancellationToken;
        var transportState = new TransportTestState();
        var eventState = new EventTestState();
        await using var host = await KoanIntegrationHost.Configure()
            .WithEnvironment("Test")
            .WithSetting("Koan:Communication:Channels:priority:TransportProvider", "in-process")
            .ConfigureServices(services =>
            {
                services.AddSingleton(transportState);
                services.AddSingleton(eventState);
                services.AddKoan();
            })
            .StartAsync(ct);
        using var scope = AppHost.PushScope(host.Services);

        var transport = await new IsolationOrder().Transport.Send(ct, channel: "priority");
        var transportSettlement = await transport.WaitForSettlement(ct);
        var occurrence = await new IsolationEventOrder().Events.Raise<IsolationEvent>(ct, channel: "priority");
        var eventSettlement = await occurrence.WaitForSettlement(ct);

        transport.Channel.Should().Be("priority");
        transport.Adapter.Should().Be("in-process");
        transportSettlement.Delivered.Should().Be(1);
        occurrence.Channel.Should().Be("priority");
        occurrence.Adapter.Should().Be("in-process");
        eventSettlement.Delivered.Should().Be(1);
        transportState.IsolationHandled.Should().Be(1);
        eventState.IsolationHandled.Should().Be(1);

        var facts = host.Services.GetRequiredService<Koan.Core.Diagnostics.IKoanRuntimeFacts>().Current.Facts;
        facts.Should().Contain(fact =>
            fact.Code == "koan.communication.transport.selected"
            && fact.Subject == "communication:transport:priority"
            && fact.ReasonCode == "explicit-binding");
        facts.Should().Contain(fact =>
            fact.Code == "koan.communication.events.selected"
            && fact.Subject == "communication:events:priority"
            && fact.ReasonCode == "built-in-floor");
        facts.Should().Contain(fact =>
            fact.Code == "koan.communication.transport.receiver.discovered"
            && fact.Subject.Contains("priority", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Unknown_channel_rejects_before_source_enumeration_with_the_configuration_path()
    {
        var ct = TestContext.Current.CancellationToken;
        var state = new TransportTestState();
        await using var host = await CommunicationTestHost.Start(state, ct);
        using var scope = AppHost.PushScope(host.Services);
        var yielded = false;

        async IAsyncEnumerable<IsolationOrder> Source()
        {
            yielded = true;
            yield return new IsolationOrder();
            await Task.CompletedTask;
        }

        var send = () => Source().Transport.Send(ct, channel: "missing");
        var failure = (await send.Should().ThrowAsync<InvalidOperationException>()).Which;

        yielded.Should().BeFalse();
        failure.Message.Should().Contain("missing")
            .And.Contain("Transport")
            .And.Contain("Koan:Communication:Channels:missing");

        var invalid = () => Source().Transport.Send(ct, channel: " ");
        var invalidFailure = (await invalid.Should().ThrowAsync<InvalidOperationException>()).Which;

        yielded.Should().BeFalse();
        invalidFailure.Message.Should().Contain("channel ' '")
            .And.Contain("must start with a letter or digit");
    }

    [Fact]
    public async Task Invalid_declared_channel_fails_host_startup_with_the_named_rule()
    {
        var ct = TestContext.Current.CancellationToken;
        var state = new TransportTestState();
        var start = () => CommunicationTestHost.Start(
            state,
            ct,
            services => services.Configure<CommunicationOptions>(options =>
                options.Channels["not a channel"] = new CommunicationChannelOptions()));

        var failure = (await start.Should().ThrowAsync<InvalidOperationException>()).Which;
        failure.Message.Should().Contain("not a channel")
            .And.Contain("letters, digits, '.', '_', or '-'");
    }
}
