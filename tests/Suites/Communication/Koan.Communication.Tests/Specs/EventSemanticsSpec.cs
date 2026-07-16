using Koan.Communication.Tests.Support;
using static Koan.Communication.Tests.Support.EventHandlerFixtures;

namespace Koan.Communication.Tests.Specs;

public sealed class EventSemanticsSpec
{
    [Fact]
    public async Task Raise_returns_acceptance_before_handler_completion_and_delivers_a_snapshot_copy()
    {
        var ct = TestContext.Current.CancellationToken;
        var state = new EventTestState();
        await using var host = await CommunicationTestHost.Start(state, ct);
        using var hostScope = AppHost.PushScope(host.Services);
        var order = new BlockingEventOrder { Name = "accepted-copy" };

        var acceptance = await order.Events.Raise<OrderApproved>(ct);
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
    public async Task Every_subscription_gets_fresh_entity_and_details_copies_for_one_occurrence()
    {
        var ct = TestContext.Current.CancellationToken;
        var state = new EventTestState();
        await using var host = await CommunicationTestHost.Start(state, ct);
        using var hostScope = AppHost.PushScope(host.Services);
        var order = new CopyEventOrder { Name = "original" };
        var details = new CopyDetails { Note = "original-details" };

        var acceptance = await order.Events.Raise(details, ct);
        order.Name = "sender-mutated";
        details.Note = "sender-mutated";
        var settlement = await acceptance.WaitForSettlement(ct);

        acceptance.SubscriptionGroups.Should().Be(2);
        state.CopyEntityObservations.Should().Equal("A:original", "B:original");
        state.CopyDetailsObservations.Should().Equal("A:original-details", "B:original-details");
        state.CopyOccurrenceIds.Should().HaveCount(2);
        state.CopyOccurrenceIds.Distinct().Should().ContainSingle();
        settlement.Delivered.Should().Be(2);
    }

    [Fact]
    public async Task Finite_sources_preserve_order_multiplicity_and_create_one_occurrence_per_item()
    {
        var ct = TestContext.Current.CancellationToken;
        var state = new EventTestState();
        await using var host = await CommunicationTestHost.Start(state, ct);
        using var hostScope = AppHost.PushScope(host.Services);
        var repeated = new SequenceEventOrder { Value = 2 };

        var acceptance = await new[]
        {
            new SequenceEventOrder { Value = 1 },
            repeated,
            repeated
        }.Events.Raise<SequenceEvent>(ct);
        var settlement = await acceptance.WaitForSettlement(ct);

        acceptance.Enumerated.Should().Be(3);
        acceptance.Accepted.Should().Be(3);
        state.SequenceObservations.Should().Equal(1, 2, 2);
        state.SequenceOrdinals.Should().Equal(0, 1, 2);
        state.SequenceOccurrenceIds.Should().HaveCount(3).And.OnlyHaveUniqueItems();
        settlement.Delivered.Should().Be(3);
    }

    [Fact]
    public async Task Every_deliberate_raise_has_new_operation_and_occurrence_identity()
    {
        var ct = TestContext.Current.CancellationToken;
        var state = new EventTestState();
        await using var host = await CommunicationTestHost.Start(state, ct);
        using var hostScope = AppHost.PushScope(host.Services);
        var order = new SequenceEventOrder { Value = 7 };

        var first = await order.Events.Raise<SequenceEvent>(ct);
        await first.WaitForSettlement(ct);
        var second = await order.Events.Raise<SequenceEvent>(ct);
        await second.WaitForSettlement(ct);

        first.OperationId.Should().NotBe(second.OperationId);
        state.SequenceOccurrenceIds.Should().HaveCount(2).And.OnlyHaveUniqueItems();
        state.SequenceObservations.Should().Equal(7, 7);
    }

    [Fact]
    public async Task Zero_subscriptions_accept_a_valid_zero_target_occurrence()
    {
        var ct = TestContext.Current.CancellationToken;
        var state = new EventTestState();
        await using var host = await CommunicationTestHost.Start(state, ct);
        using var hostScope = AppHost.PushScope(host.Services);

        var acceptance = await new NoSubscriberEventOrder().Events.Raise<NoSubscriberEvent>(ct);
        var settlement = await acceptance.WaitForSettlement(ct);

        acceptance.Accepted.Should().Be(1);
        acceptance.SubscriptionGroups.Should().Be(0);
        settlement.Expected.Should().Be(0);
        settlement.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Details_policy_rejects_missing_details_before_enumeration_and_accepts_explicit_or_optional_details()
    {
        var ct = TestContext.Current.CancellationToken;
        var state = new EventTestState();
        await using var host = await CommunicationTestHost.Start(state, ct);
        using var hostScope = AppHost.PushScope(host.Services);
        var enumerated = false;

        IEnumerable<RequiredDetailsOrder> Source()
        {
            enumerated = true;
            yield return new RequiredDetailsOrder();
        }

        var missing = () => Source().Events.Raise<RejectionDetails>(ct);
        var error = (await missing.Should().ThrowAsync<EventException>()).Which;
        error.Failure.Should().Be(EventException.FailureKind.DetailsRequired);
        error.Acceptance.Accepted.Should().Be(0);
        enumerated.Should().BeFalse();

        var required = await new RequiredDetailsOrder().Events.Raise(new RejectionDetails("not ready"), ct);
        var payloadless = await new OptionalDetailsOrder().Events.Raise<OptionalDetails>(ct);
        var optional = await new OptionalDetailsOrder().Events.Raise(new OptionalDetails("note"), ct);
        await required.WaitForSettlement(ct);
        await payloadless.WaitForSettlement(ct);
        await optional.WaitForSettlement(ct);

        state.RequiredDetailsObservations.Should().Equal("not ready");
        state.OptionalDetailsObservations.Should().Equal("payloadless", "note");
    }

    [Fact]
    public async Task Subscription_filter_and_handler_failure_are_independent_settlement_outcomes()
    {
        var ct = TestContext.Current.CancellationToken;
        var state = new EventTestState();
        await using var host = await CommunicationTestHost.Start(state, ct);
        using var hostScope = AppHost.PushScope(host.Services);

        var filteredAcceptance = await new[]
        {
            new FilterEventOrder { Accepted = false },
            new FilterEventOrder { Accepted = true }
        }.Events.Raise<FilterEvent>(ct);
        var filtered = await filteredAcceptance.WaitForSettlement(ct);

        var failedAcceptance = await new FailureEventOrder().Events.Raise<FailureEvent>(ct);
        var failed = await failedAcceptance.WaitForSettlement(ct);

        state.FilterHandled.Should().Be(1);
        filtered.Filtered.Should().Be(1);
        filtered.Delivered.Should().Be(1);
        failed.Failed.Should().Be(1);
        failed.Delivered.Should().Be(1);
        state.FailureSurvivorHandled.Should().Be(1);
    }
}
