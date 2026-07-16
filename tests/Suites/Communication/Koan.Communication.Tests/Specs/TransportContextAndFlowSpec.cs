using System.Runtime.CompilerServices;
using Koan.Communication.Tests.Support;
using Koan.Tenancy;
using static Koan.Communication.Tests.Support.TransportReceiverFixtures;

namespace Koan.Communication.Tests.Specs;

public sealed class TransportContextAndFlowSpec
{
    [Fact]
    public async Task Tenant_context_is_sealed_at_send_and_restored_only_for_that_delivery()
    {
        var ct = TestContext.Current.CancellationToken;
        var state = new TransportTestState();
        await using var host = await TransportTestHost.Start(state, ct);
        using var hostScope = AppHost.PushScope(host.Services);

        TransportAcceptance tenantAcceptance;
        using (Tenant.Use("tenant-a"))
        {
            tenantAcceptance = await new TenantOrder().Transport.Send(ct);
        }

        var hostAcceptance = await new TenantOrder().Transport.Send(ct);
        await tenantAcceptance.WaitForSettlement(ct);
        await hostAcceptance.WaitForSettlement(ct);

        state.TenantObservations.Should().Equal("tenant-a", null);
        Tenant.Current.Should().BeNull();
    }

    [Fact]
    public async Task Stream_cancellation_reports_the_accepted_prefix_and_settles_only_that_operation()
    {
        var outerCt = TestContext.Current.CancellationToken;
        using var sendCts = CancellationTokenSource.CreateLinkedTokenSource(outerCt);
        var state = new TransportTestState();
        await using var host = await TransportTestHost.Start(
            state,
            outerCt,
            services => services.Configure<CommunicationOptions>(options => options.InProcessCapacity = 1));
        using var hostScope = AppHost.PushScope(host.Services);
        var thirdYielded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var send = Source(thirdYielded, sendCts.Token).Transport.Send(sendCts.Token);
        await state.CancellationStarted.Task.WaitAsync(outerCt);
        await thirdYielded.Task.WaitAsync(outerCt);
        sendCts.Cancel();

        Func<Task> waitForSend = async () => await send;
        var error = (await waitForSend.Should().ThrowAsync<TransportCanceledException>()).Which;
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
    public async Task Payload_limit_rejects_before_local_acceptance_with_a_bounded_receipt()
    {
        var ct = TestContext.Current.CancellationToken;
        var state = new TransportTestState();
        await using var host = await TransportTestHost.Start(
            state,
            ct,
            services => services.Configure<CommunicationOptions>(options => options.MaxPayloadBytes = 8));
        using var hostScope = AppHost.PushScope(host.Services);

        var action = () => new BlockingOrder { Name = "larger-than-eight-bytes" }.Transport.Send(ct);
        var error = (await action.Should().ThrowAsync<TransportException>()).Which;

        error.Failure.Should().Be(TransportException.FailureKind.PayloadTooLarge);
        error.Acceptance.Enumerated.Should().Be(1);
        error.Acceptance.Accepted.Should().Be(0);
        error.Acceptance.Rejected.Should().Be(1);
        (await error.Acceptance.WaitForSettlement(ct)).Expected.Should().Be(0);
    }

    private static async IAsyncEnumerable<CancellationOrder> Source(
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

            yield return new CancellationOrder { Value = value };
            await Task.Yield();
        }
    }
}
