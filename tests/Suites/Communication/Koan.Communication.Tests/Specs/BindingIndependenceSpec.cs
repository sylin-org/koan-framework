using Koan.Communication.Tests.Support;
using static Koan.Communication.Tests.Support.IndependenceFixtures;

namespace Koan.Communication.Tests.Specs;

public sealed class BindingIndependenceSpec
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task A_hung_event_handler_does_not_delay_another_subscriptions_deliveries()
    {
        var ct = TestContext.Current.CancellationToken;
        var state = new IndependenceState();
        await using var host = await CommunicationTestHost.Start(state, ct);
        using var hostScope = AppHost.PushScope(host.Services);
        try
        {
            await new StallOrder { Name = "stalled" }.Events.Raise<Stall>(ct);
            await state.Stalled.Task.WaitAsync(Bound, ct);

            var prompt = await new StallOrder { Name = "after" }.Events.Raise<Prompt>(ct);
            var settlement = await prompt.WaitForSettlement(ct).WaitAsync(Bound, ct);

            settlement.Delivered.Should().Be(1);
            state.Observations.Should().Equal("prompt:after");
        }
        finally
        {
            state.Release.TrySetResult(true);
        }
    }

    [Fact]
    public async Task A_hung_receiver_group_does_not_delay_another_group_receiving_the_same_snapshots()
    {
        var ct = TestContext.Current.CancellationToken;
        var state = new IndependenceState();
        await using var host = await CommunicationTestHost.Start(state, ct);
        using var hostScope = AppHost.PushScope(host.Services);
        try
        {
            var first = await new StallTransportOrder { Name = "one" }.Transport.Send(ct);
            await state.Stalled.Task.WaitAsync(Bound, ct);
            var second = await new StallTransportOrder { Name = "two" }.Transport.Send(ct);

            await WaitUntil(() => state.Observations.Count == 2, ct);

            state.Observations.Should().Equal("tally:one", "tally:two");
            state.StalledInvocations.Should().Be(1);
            first.ReceiverGroups.Should().Be(2);
            second.ReceiverGroups.Should().Be(2);
        }
        finally
        {
            state.Release.TrySetResult(true);
        }
    }

    [Fact]
    public async Task Raise_returns_promptly_while_a_subscriptions_backlog_is_over_budget_and_settles_that_overflow_as_failed()
    {
        var ct = TestContext.Current.CancellationToken;
        var state = new IndependenceState();
        await using var host = await CommunicationTestHost.Start(
            state,
            ct,
            services => services.Configure<CommunicationOptions>(options => options.InProcessBacklogBytes = 1));
        using var hostScope = AppHost.PushScope(host.Services);
        try
        {
            await new CrowdOrder { Name = "1" }.Events.Raise<Jammed>(ct);
            await state.Stalled.Task.WaitAsync(Bound, ct);
            var queued = await new CrowdOrder { Name = "2" }.Events.Raise<Jammed>(ct);

            var raise = new CrowdOrder { Name = "3" }.Events.Raise<Jammed>(ct);
            var finished = await Task.WhenAny(raise, Task.Delay(Bound, ct));

            finished.Should().BeSameAs(raise, "a subscription's backlog never makes the raiser wait");
            var overflowed = await raise;
            overflowed.Accepted.Should().Be(1);
            overflowed.SubscriptionGroups.Should().Be(1);
            var settlement = await overflowed.WaitForSettlement(ct).WaitAsync(Bound, ct);
            settlement.Expected.Should().Be(1);
            settlement.Delivered.Should().Be(0);
            settlement.Failed.Should().Be(1);
            settlement.Overflowed.Should().Be(1);
            settlement.Succeeded.Should().BeFalse();

            state.Release.TrySetResult(true);
            (await queued.WaitForSettlement(ct).WaitAsync(Bound, ct)).Delivered.Should().Be(1);
            state.Observations.Should().Equal("jam:1", "jam:2");
        }
        finally
        {
            state.Release.TrySetResult(true);
        }
    }

    [Fact]
    public async Task A_snapshot_over_four_megabytes_is_sent_and_raised_in_process()
    {
        var ct = TestContext.Current.CancellationToken;
        var state = new IndependenceState();
        await using var host = await CommunicationTestHost.Start(state, ct);
        using var hostScope = AppHost.PushScope(host.Services);
        var order = new LargeOrder { Body = new string('x', 5 * 1024 * 1024) };

        var sent = await order.Transport.Send(ct);
        var raised = await order.Events.Raise<LargeRaised>(ct);

        (await sent.WaitForSettlement(ct)).Delivered.Should().Be(1);
        (await raised.WaitForSettlement(ct)).Delivered.Should().Be(1);
        state.PayloadLengths.Should().Equal(order.Body.Length, order.Body.Length);
    }

    [Fact]
    public async Task A_handler_past_the_handler_timeout_is_cancelled_its_delivery_fails_and_its_binding_moves_on()
    {
        var ct = TestContext.Current.CancellationToken;
        var state = new IndependenceState();
        await using var host = await CommunicationTestHost.Start(
            state,
            ct,
            services => services.Configure<CommunicationOptions>(options =>
                options.HandlerTimeout = TimeSpan.FromMilliseconds(200)));
        using var hostScope = AppHost.PushScope(host.Services);

        var acceptance = await new[] { new TimeoutOrder { Value = 1 }, new TimeoutOrder { Value = 2 } }
            .Events.Raise<TimedRaised>(ct);
        var settlement = await acceptance.WaitForSettlement(ct).WaitAsync(Bound, ct);

        settlement.Expected.Should().Be(2);
        settlement.Failed.Should().Be(1);
        settlement.Overflowed.Should().Be(0);
        settlement.Delivered.Should().Be(1);
        state.Observations.Should().Equal("timed:2");
    }

    private static async Task WaitUntil(Func<bool> condition, CancellationToken ct)
    {
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(ct);
        bound.CancelAfter(Bound);
        while (!condition())
            await Task.Delay(TimeSpan.FromMilliseconds(20), bound.Token);
    }
}
