using Koan.Communication.Tests.Support;
using Koan.Tenancy;
using static Koan.Communication.Tests.Support.EventHandlerFixtures;

namespace Koan.Communication.Tests.Specs;

public sealed class EventContextAndFlowSpec
{
    [Fact]
    public async Task Tenant_context_is_sealed_at_raise_and_restored_only_for_that_occurrence()
    {
        var ct = TestContext.Current.CancellationToken;
        var state = new EventTestState();
        await using var host = await CommunicationTestHost.Start(state, ct);
        using var hostScope = AppHost.PushScope(host.Services);

        EventAcceptance tenantAcceptance;
        using (Tenant.Use("tenant-a"))
        {
            tenantAcceptance = await new TenantEventOrder().Events.Raise<TenantEvent>(ct);
        }

        var unscoped = () => new TenantEventOrder().Events.Raise<TenantEvent>(ct);
        await unscoped.Should().ThrowAsync<Koan.Core.Semantics.Segmentation.SegmentationRequiredException>();
        await tenantAcceptance.WaitForSettlement(ct);

        state.TenantObservations.Should().Equal("tenant-a");
        Tenant.Current.Should().BeNull();
    }

    [Fact]
    public async Task Stream_cancellation_reports_the_accepted_occurrence_prefix()
    {
        var outerCt = TestContext.Current.CancellationToken;
        using var raiseCts = CancellationTokenSource.CreateLinkedTokenSource(outerCt);
        var state = new EventTestState();
        await using var host = await CommunicationTestHost.Start(state, outerCt);
        using var hostScope = AppHost.PushScope(host.Services);
        var thirdReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var proceed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var raise = Source(thirdReady, proceed.Task).Events.Raise<CancellationEvent>(raiseCts.Token);
        await state.CancellationStarted.Task.WaitAsync(outerCt);
        await thirdReady.Task.WaitAsync(outerCt);
        raiseCts.Cancel();
        proceed.TrySetResult(true);

        Func<Task> waitForRaise = async () => await raise;
        var error = (await waitForRaise.Should().ThrowAsync<EventCanceledException>()).Which;
        error.Acceptance.SourceCompleted.Should().BeFalse();
        error.Acceptance.Enumerated.Should().Be(3);
        error.Acceptance.Accepted.Should().Be(2);
        error.Acceptance.Rejected.Should().Be(1);

        state.CancellationRelease.TrySetResult(true);
        var settlement = await error.Acceptance.WaitForSettlement(outerCt);
        settlement.Expected.Should().Be(2);
        settlement.Delivered.Should().Be(2);
    }

    [Fact]
    public async Task A_provider_declared_payload_limit_counts_details_and_rejects_before_publication()
    {
        var ct = TestContext.Current.CancellationToken;
        var state = new EventTestState();
        var limited = new LimitedAdapter(maxPayloadBytes: 1024);
        await using var host = await CommunicationTestHost.Start(
            state,
            ct,
            services =>
            {
                services.AddSingleton<ICommunicationAdapter>(limited);
                services.Configure<CommunicationOptions>(options => options.EventsProvider = "limited");
            });
        using var hostScope = AppHost.PushScope(host.Services);

        var fits = await new OptionalDetailsOrder().Events.Raise(new OptionalDetails("small"), ct);
        var action = () => new OptionalDetailsOrder().Events.Raise(
            new OptionalDetails(new string('x', 2048)),
            ct);
        var error = (await action.Should().ThrowAsync<EventException>()).Which;

        fits.Accepted.Should().Be(1);
        error.Failure.Should().Be(EventException.FailureKind.PayloadTooLarge);
        error.Message.Should().Contain("1024-byte transport limit").And.Contain("'limited'");
        error.Acceptance.Enumerated.Should().Be(1);
        error.Acceptance.Accepted.Should().Be(0);
        error.Acceptance.Rejected.Should().Be(1);
        limited.PublishCount.Should().Be(1);
    }

    private static async IAsyncEnumerable<CancellationEventOrder> Source(
        TaskCompletionSource<bool> thirdReady,
        Task proceed)
    {
        for (var value = 1; value <= 3; value++)
        {
            if (value == 3)
            {
                thirdReady.TrySetResult(true);
                await proceed;
            }

            yield return new CancellationEventOrder { Value = value };
        }
    }
}
