using System.Diagnostics;
using Koan.Communication.Tests.Support;
using static Koan.Communication.Tests.Support.BurstFixtures;

namespace Koan.Communication.Tests.Specs;

/// <summary>
/// A handler that keeps up on average never loses a delivery, however large the burst: a group's backlog is bounded
/// by memory, not by a count.
/// </summary>
public sealed class BurstSpec
{
    private const int Rows = 20_000;
    private const int PageSize = 100;

    [Fact]
    public async Task A_twenty_thousand_row_burst_reaches_a_handler_that_does_nothing_in_full_and_in_order()
    {
        var ct = TestContext.Current.CancellationToken;
        var state = new BurstState();
        await using var host = await CommunicationTestHost.Start(state, ct);
        using var hostScope = AppHost.PushScope(host.Services);

        var settlements = await RaiseInPages<Burst>(ct);

        settlements.Sum(static settlement => settlement.Delivered).Should().Be(Rows);
        settlements.Sum(static settlement => settlement.Failed).Should().Be(0);
        state.Received.Should().Equal(Enumerable.Range(0, Rows));
    }

    [Fact]
    public async Task A_twenty_thousand_row_burst_reaches_a_one_millisecond_handler_in_full_and_in_order()
    {
        var ct = TestContext.Current.CancellationToken;
        var state = new BurstState();
        await using var host = await CommunicationTestHost.Start(state, ct);
        using var hostScope = AppHost.PushScope(host.Services);

        var settlements = await RaiseInPages<SlowBurst>(ct);

        settlements.Sum(static settlement => settlement.Delivered).Should().Be(Rows);
        settlements.Sum(static settlement => settlement.Failed).Should().Be(0);
        state.Received.Should().Equal(Enumerable.Range(0, Rows));
    }

    [Fact]
    public async Task Forced_stop_abandons_a_handler_that_blocks_its_thread_and_fails_its_delivery()
    {
        var ct = TestContext.Current.CancellationToken;
        var state = new BurstState();
        await using var host = await CommunicationTestHost.Start(state, ct);
        using var hostScope = AppHost.PushScope(host.Services);
        try
        {
            var acceptance = await new BurstOrder { Value = 1 }.Events.Raise<ThreadBlocked>(ct);
            await state.Stalled.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);

            using var stopToken = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            var clock = Stopwatch.StartNew();
            var stopping = Swallow(host.StopAsync(stopToken.Token));
            var finished = await Task.WhenAny(stopping, Task.Delay(TimeSpan.FromSeconds(10), ct));
            clock.Stop();

            finished.Should().BeSameAs(stopping, "a forced stop never waits on a handler that blocks its thread");
            clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
            var settlement = await acceptance.WaitForSettlement(ct).WaitAsync(TimeSpan.FromSeconds(10), ct);
            settlement.Failed.Should().Be(1);
            settlement.Delivered.Should().Be(0);
        }
        finally
        {
            state.Gate.Set();
        }
    }

    [Fact]
    public async Task A_stalled_group_fails_past_its_budget_while_another_group_receives_every_row_in_order()
    {
        const int rows = 2_000;
        var ct = TestContext.Current.CancellationToken;
        var state = new BurstState();
        await using var host = await CommunicationTestHost.Start(
            state,
            ct,
            services => services.Configure<CommunicationOptions>(options => options.InProcessBacklogBytes = 256 * 1024));
        using var hostScope = AppHost.PushScope(host.Services);
        try
        {
            var acceptances = new List<EventAcceptance>();
            for (var start = 0; start < rows; start += PageSize)
            {
                acceptances.Add(await Page(start).Events.Raise<StalledBurst>(ct));
                if (start == 0) await state.Stalled.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
                var through = start + PageSize;
                await WaitUntil(() => state.Tallied.Count == through, ct);
            }

            state.Release.TrySetResult(true);
            var settlements = await Task.WhenAll(acceptances.Select(acceptance => acceptance.WaitForSettlement(ct)))
                .WaitAsync(TimeSpan.FromMinutes(1), ct);

            var expected = settlements.Sum(static settlement => settlement.Expected);
            var delivered = settlements.Sum(static settlement => settlement.Delivered);
            var failed = settlements.Sum(static settlement => settlement.Failed);
            var overflowed = settlements.Sum(static settlement => settlement.Overflowed);
            expected.Should().Be(2 * rows);
            overflowed.Should().BePositive();
            failed.Should().Be(overflowed);
            (delivered + failed).Should().Be(expected);
            state.Tallied.Should().Equal(Enumerable.Range(0, rows));
            state.Received.Should().Equal(Enumerable.Range(0, (int)(delivered - rows)));
        }
        finally
        {
            state.Release.TrySetResult(true);
        }
    }

    private static BurstOrder[] Page(int start)
        => Enumerable.Range(start, PageSize).Select(static value => new BurstOrder { Value = value }).ToArray();

    private static async Task WaitUntil(Func<bool> condition, CancellationToken ct)
    {
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(ct);
        bound.CancelAfter(TimeSpan.FromSeconds(30));
        while (!condition())
            await Task.Delay(TimeSpan.FromMilliseconds(20), bound.Token);
    }

    private static async Task<EventSettlement[]> RaiseInPages<TEvent>(CancellationToken ct)
        where TEvent : class
    {
        var acceptances = new List<EventAcceptance>(Rows / PageSize);
        for (var start = 0; start < Rows; start += PageSize)
        {
            acceptances.Add(await Page(start).Events.Raise<TEvent>(ct));
        }

        return await Task.WhenAll(acceptances.Select(acceptance => acceptance.WaitForSettlement(ct)))
            .WaitAsync(TimeSpan.FromMinutes(2), ct);
    }

    private static async Task Swallow(Task stopping)
    {
        try
        {
            await stopping;
        }
        catch (Exception error) when (error is OperationCanceledException or AggregateException)
        {
            // A forced stop reports its cancellation; the spec measures only that it returns.
        }
    }
}
