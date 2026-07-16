using Koan.Communication.Tests.Support;
using static Koan.Communication.Tests.Support.TransportReceiverFixtures;

namespace Koan.Communication.Tests.Specs;

public sealed class TransportSemanticsSpec
{
    [Fact]
    public async Task Send_returns_acceptance_before_handler_completion_and_delivers_a_snapshot_copy()
    {
        var ct = TestContext.Current.CancellationToken;
        var state = new TransportTestState();
        await using var host = await TransportTestHost.Start(state, ct);
        using var hostScope = AppHost.PushScope(host.Services);
        var order = new BlockingOrder { Name = "accepted-copy" };

        var acceptance = await order.Transport.Send(ct);
        await state.BlockingStarted.Task.WaitAsync(ct);
        acceptance.Accepted.Should().Be(1);
        acceptance.SourceCompleted.Should().BeTrue();
        acceptance.Adapter.Should().Be("in-process");
        acceptance.Assurance.Should().Be("process-memory");
        state.BlockingObservations.Should().BeEmpty();

        order.Name = "sender-mutated";
        state.BlockingRelease.TrySetResult(true);
        var settlement = await acceptance.WaitForSettlement(ct);

        state.BlockingObservations.Should().Equal("accepted-copy");
        settlement.Expected.Should().Be(1);
        settlement.Delivered.Should().Be(1);
        settlement.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Every_receiver_group_gets_an_independent_deserialized_copy()
    {
        var ct = TestContext.Current.CancellationToken;
        var state = new TransportTestState();
        await using var host = await TransportTestHost.Start(state, ct);
        using var hostScope = AppHost.PushScope(host.Services);

        var acceptance = await new CopyOrder { Name = "original" }.Transport.Send(ct);
        var settlement = await acceptance.WaitForSettlement(ct);

        acceptance.ReceiverGroups.Should().Be(2);
        state.CopyObservations.Should().Equal("A:original", "B:original");
        settlement.Delivered.Should().Be(2);
    }

    [Fact]
    public async Task Finite_sources_preserve_order_multiplicity_and_pointwise_counts()
    {
        var ct = TestContext.Current.CancellationToken;
        var state = new TransportTestState();
        await using var host = await TransportTestHost.Start(state, ct);
        using var hostScope = AppHost.PushScope(host.Services);
        var repeated = new SequenceOrder { Value = 2 };

        var acceptance = await new[]
        {
            new SequenceOrder { Value = 1 },
            repeated,
            repeated
        }.Transport.Send(ct);
        var settlement = await acceptance.WaitForSettlement(ct);

        acceptance.Enumerated.Should().Be(3);
        acceptance.Accepted.Should().Be(3);
        acceptance.Rejected.Should().Be(0);
        state.SequenceObservations.Should().Equal(1, 2, 2);
        settlement.Delivered.Should().Be(3);
    }

    [Fact]
    public async Task Every_deliberate_send_has_a_new_operation_identity()
    {
        var ct = TestContext.Current.CancellationToken;
        var state = new TransportTestState();
        await using var host = await TransportTestHost.Start(state, ct);
        using var hostScope = AppHost.PushScope(host.Services);
        var order = new SequenceOrder { Value = 7 };

        var first = await order.Transport.Send(ct);
        var second = await order.Transport.Send(ct);
        await first.WaitForSettlement(ct);
        await second.WaitForSettlement(ct);

        first.OperationId.Should().NotBe(second.OperationId);
        state.SequenceObservations.Should().Equal(7, 7);
    }

    [Fact]
    public async Task Receiver_where_records_filtered_settlement_without_entering_business_handler()
    {
        var ct = TestContext.Current.CancellationToken;
        var state = new TransportTestState();
        await using var host = await TransportTestHost.Start(state, ct);
        using var hostScope = AppHost.PushScope(host.Services);

        var acceptance = await new[]
        {
            new FilterOrder { Accepted = false },
            new FilterOrder { Accepted = true }
        }.Transport.Send(ct);
        var settlement = await acceptance.WaitForSettlement(ct);

        state.FilterHandled.Should().Be(1);
        settlement.Filtered.Should().Be(1);
        settlement.Delivered.Should().Be(1);
        settlement.Failed.Should().Be(0);
    }

    [Fact]
    public async Task Handler_failure_is_settlement_not_publication_failure()
    {
        var ct = TestContext.Current.CancellationToken;
        var state = new TransportTestState();
        await using var host = await TransportTestHost.Start(state, ct);
        using var hostScope = AppHost.PushScope(host.Services);

        var acceptance = await new FailureOrder().Transport.Send(ct);
        var settlement = await acceptance.WaitForSettlement(ct);

        acceptance.Accepted.Should().Be(1);
        settlement.Failed.Should().Be(1);
        settlement.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task Missing_receiver_fails_before_the_source_is_enumerated()
    {
        var ct = TestContext.Current.CancellationToken;
        var state = new TransportTestState();
        await using var host = await TransportTestHost.Start(state, ct);
        using var hostScope = AppHost.PushScope(host.Services);
        var enumerated = false;

        IEnumerable<NoReceiverOrder> Source()
        {
            enumerated = true;
            yield return new NoReceiverOrder();
        }

        var action = () => Source().Transport.Send(ct);
        var error = (await action.Should().ThrowAsync<TransportException>()).Which;

        error.Failure.Should().Be(TransportException.FailureKind.NoReceivers);
        error.Acceptance.Accepted.Should().Be(0);
        enumerated.Should().BeFalse();
    }
}
