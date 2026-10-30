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
        await using var host = await CommunicationTestHost.Start(state, ct);
        using var hostScope = AppHost.PushScope(host.Services);

        TransportAcceptance tenantAcceptance;
        using (Tenant.Use("tenant-a"))
        {
            tenantAcceptance = await new TenantOrder().Transport.Send(ct);
        }

        var unscoped = () => new TenantOrder().Transport.Send(ct);
        await unscoped.Should().ThrowAsync<Koan.Core.Semantics.Segmentation.SegmentationRequiredException>();
        await tenantAcceptance.WaitForSettlement(ct);

        state.TenantObservations.Should().Equal("tenant-a");
        Tenant.Current.Should().BeNull();
    }

    [Fact]
    public async Task Stream_cancellation_reports_the_accepted_prefix_and_settles_only_that_operation()
    {
        var outerCt = TestContext.Current.CancellationToken;
        using var sendCts = CancellationTokenSource.CreateLinkedTokenSource(outerCt);
        var state = new TransportTestState();
        await using var host = await CommunicationTestHost.Start(state, outerCt);
        using var hostScope = AppHost.PushScope(host.Services);
        var thirdReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var proceed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var send = Source(thirdReady, proceed.Task).Transport.Send(sendCts.Token);
        await state.CancellationStarted.Task.WaitAsync(outerCt);
        await thirdReady.Task.WaitAsync(outerCt);
        sendCts.Cancel();
        proceed.TrySetResult(true);

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
    public async Task A_provider_declared_payload_limit_rejects_an_oversized_snapshot_before_publication()
    {
        var ct = TestContext.Current.CancellationToken;
        var state = new TransportTestState();
        var limited = new LimitedAdapter(maxPayloadBytes: 1024);
        await using var host = await CommunicationTestHost.Start(
            state,
            ct,
            services =>
            {
                services.AddSingleton<ICommunicationAdapter>(limited);
                services.Configure<CommunicationOptions>(options => options.TransportProvider = "limited");
            });
        using var hostScope = AppHost.PushScope(host.Services);

        var fits = await new BlockingOrder { Name = "small" }.Transport.Send(ct);
        var action = () => new BlockingOrder { Name = new string('x', 2048) }.Transport.Send(ct);
        var error = (await action.Should().ThrowAsync<TransportException>()).Which;

        fits.Accepted.Should().Be(1);
        error.Failure.Should().Be(TransportException.FailureKind.PayloadTooLarge);
        error.Message.Should().Contain("1024-byte transport limit").And.Contain("'limited'");
        error.Acceptance.Enumerated.Should().Be(1);
        error.Acceptance.Accepted.Should().Be(0);
        error.Acceptance.Rejected.Should().Be(1);
        limited.PublishCount.Should().Be(1);
    }

    private static async IAsyncEnumerable<CancellationOrder> Source(
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

            yield return new CancellationOrder { Value = value };
        }
    }
}
