using System.Runtime.CompilerServices;
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

        var hostAcceptance = await new TenantEventOrder().Events.Raise<TenantEvent>(ct);
        await tenantAcceptance.WaitForSettlement(ct);
        await hostAcceptance.WaitForSettlement(ct);

        state.TenantObservations.Should().Equal("tenant-a", null);
        Tenant.Current.Should().BeNull();
    }

    [Fact]
    public async Task Stream_cancellation_reports_the_accepted_occurrence_prefix()
    {
        var outerCt = TestContext.Current.CancellationToken;
        using var raiseCts = CancellationTokenSource.CreateLinkedTokenSource(outerCt);
        var state = new EventTestState();
        await using var host = await CommunicationTestHost.Start(
            state,
            outerCt,
            services => services.Configure<CommunicationOptions>(options => options.InProcessCapacity = 1));
        using var hostScope = AppHost.PushScope(host.Services);
        var thirdYielded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var raise = Source(thirdYielded, raiseCts.Token).Events.Raise<CancellationEvent>(raiseCts.Token);
        await state.CancellationStarted.Task.WaitAsync(outerCt);
        await thirdYielded.Task.WaitAsync(outerCt);
        raiseCts.Cancel();

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
    public async Task Combined_entity_and_details_payload_limit_rejects_before_local_acceptance()
    {
        var ct = TestContext.Current.CancellationToken;
        var state = new EventTestState();
        await using var host = await CommunicationTestHost.Start(
            state,
            ct,
            services => services.Configure<CommunicationOptions>(options => options.MaxPayloadBytes = 96));
        using var hostScope = AppHost.PushScope(host.Services);

        var action = () => new OptionalDetailsOrder().Events.Raise(
            new OptionalDetails(new string('x', 256)),
            ct);
        var error = (await action.Should().ThrowAsync<EventException>()).Which;

        error.Failure.Should().Be(EventException.FailureKind.PayloadTooLarge);
        error.Acceptance.Enumerated.Should().Be(1);
        error.Acceptance.Accepted.Should().Be(0);
        error.Acceptance.Rejected.Should().Be(1);
        (await error.Acceptance.WaitForSettlement(ct)).Expected.Should().Be(0);
    }

    private static async IAsyncEnumerable<CancellationEventOrder> Source(
        TaskCompletionSource<bool> thirdYielded,
        [EnumeratorCancellation] CancellationToken ct)
    {
        for (var value = 1; value <= 3; value++)
        {
            ct.ThrowIfCancellationRequested();
            if (value == 3)
            {
                thirdYielded.TrySetResult(true);
            }

            yield return new CancellationEventOrder { Value = value };
            await Task.Yield();
        }
    }
}
