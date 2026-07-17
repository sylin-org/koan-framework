using Koan.Communication.Tests.Support;
using Koan.Core.Diagnostics;
using Microsoft.Extensions.Options;
using static Koan.Communication.Tests.Support.TransportReceiverFixtures;

namespace Koan.Communication.Tests.Specs;

public sealed class TransportCompositionAndLifetimeSpec
{
    [Fact]
    public async Task Foundation_AddKoan_composes_local_transport_and_reports_its_decisions()
    {
        var ct = TestContext.Current.CancellationToken;
        var state = new TransportTestState();
        await using var host = await CommunicationTestHost.Start(state, ct);

        var facts = host.Services.GetRequiredService<IKoanRuntimeFacts>().Current;
        facts.Complete.Should().BeTrue();
        facts.Facts.Should().Contain(fact =>
            fact.Code == "koan.communication.transport.selected"
            && fact.Subject == "communication:transport:default"
            && fact.ReasonCode == "built-in-floor");
        facts.Facts.Should().Contain(fact =>
            fact.Code == "koan.communication.transport.receivers.discovered");
        facts.Facts.Should().Contain(fact =>
            fact.Code == "koan.communication.context.carriage"
            && fact.Subject == "communication:context");
        facts.Facts.Should().Contain(fact =>
            fact.Code == "koan.communication.context.guarantees"
            && fact.Subject == "communication:transport:default:context"
            && fact.Kind == KoanFactKind.Guarantee
            && fact.Summary.Contains("host-trusted", StringComparison.Ordinal)
            && fact.Summary.Contains("provider-shared", StringComparison.Ordinal)
            && fact.Summary.Contains("does not provide payload confidentiality", StringComparison.Ordinal));
        facts.Facts.Should().Contain(fact =>
            fact.Code == "koan.segmentation.realization.active"
            && fact.Subject == "segmentation:communication"
            && fact.Kind == KoanFactKind.Guarantee
            && fact.Summary.Contains("typed-context-carriage", StringComparison.Ordinal));
        facts.Facts.Should().Contain(fact =>
            fact.Code == "koan.communication.transport.bounds"
            && fact.Subject == "communication:transport:bounds"
            && fact.Summary.Contains("process-memory", StringComparison.Ordinal)
            && fact.Summary.Contains("256-snapshot", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Graceful_host_stop_drains_an_accepted_delivery()
    {
        var ct = TestContext.Current.CancellationToken;
        var state = new TransportTestState();
        await using var host = await CommunicationTestHost.Start(state, ct);
        using var hostScope = AppHost.PushScope(host.Services);

        var acceptance = await new DrainOrder().Transport.Send(ct);
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
    public async Task Repeated_hosts_keep_receiver_dependencies_and_delivery_counts_isolated()
    {
        var ct = TestContext.Current.CancellationToken;
        var first = new TransportTestState();
        await using (var host = await CommunicationTestHost.Start(first, ct))
        {
            using var hostScope = AppHost.PushScope(host.Services);
            var acceptance = await new IsolationOrder().Transport.Send(ct);
            await acceptance.WaitForSettlement(ct);
        }

        var second = new TransportTestState();
        await using (var host = await CommunicationTestHost.Start(second, ct))
        {
            using var hostScope = AppHost.PushScope(host.Services);
            var acceptance = await new IsolationOrder().Transport.Send(ct);
            await acceptance.WaitForSettlement(ct);
        }

        first.IsolationHandled.Should().Be(1);
        second.IsolationHandled.Should().Be(1);
    }

    [Fact]
    public async Task Invalid_local_bounds_fail_during_host_start_with_the_named_option()
    {
        var ct = TestContext.Current.CancellationToken;
        var state = new TransportTestState();

        var action = () => CommunicationTestHost.Start(
            state,
            ct,
            services => services.Configure<CommunicationOptions>(options => options.InProcessCapacity = 0));

        var error = (await action.Should().ThrowAsync<OptionsValidationException>()).Which;
        error.Message.Should().Contain(nameof(CommunicationOptions.InProcessCapacity));
    }
}
