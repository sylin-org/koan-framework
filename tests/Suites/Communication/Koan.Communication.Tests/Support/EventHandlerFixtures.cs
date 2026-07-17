using Koan.Tenancy;

namespace Koan.Communication.Tests.Support;

public static class EventHandlerFixtures
{
    public sealed record OrderApproved;

    public sealed class BlockingEventOrder : Entity<BlockingEventOrder>, IAmbientExempt
    {
        public string Name { get; set; } = "";
    }

    public sealed class BlockingEventHandler(EventTestState state)
        : IHandleEntityEvent<BlockingEventOrder, OrderApproved>
    {
        public async Task Handle(
            BlockingEventOrder order,
            EventOccurrence<OrderApproved> occurrence,
            CancellationToken ct)
        {
            state.BlockingStarted.TrySetResult(true);
            await state.BlockingRelease.Task.WaitAsync(ct);
            state.BlockingObservations.Enqueue(order.Name);
        }
    }

    public sealed class CopyEventOrder : Entity<CopyEventOrder>, IAmbientExempt
    {
        public string Name { get; set; } = "";
    }

    [EventDetailsRequired]
    public sealed class CopyDetails
    {
        public string Note { get; set; } = "";
    }

    public sealed class CopyAEventHandler(EventTestState state)
        : IHandleEntityEvent<CopyEventOrder, CopyDetails>
    {
        public Task Handle(
            CopyEventOrder order,
            EventOccurrence<CopyDetails> occurrence,
            CancellationToken ct)
        {
            state.CopyEntityObservations.Enqueue("A:" + order.Name);
            state.CopyDetailsObservations.Enqueue("A:" + occurrence.Details.Note);
            state.CopyOccurrenceIds.Enqueue(occurrence.OccurrenceId);
            order.Name = "changed-by-a";
            occurrence.Details.Note = "changed-by-a";
            return Task.CompletedTask;
        }
    }

    public sealed class CopyBEventHandler(EventTestState state)
        : IHandleEntityEvent<CopyEventOrder, CopyDetails>
    {
        public Task Handle(
            CopyEventOrder order,
            EventOccurrence<CopyDetails> occurrence,
            CancellationToken ct)
        {
            state.CopyEntityObservations.Enqueue("B:" + order.Name);
            state.CopyDetailsObservations.Enqueue("B:" + occurrence.Details.Note);
            state.CopyOccurrenceIds.Enqueue(occurrence.OccurrenceId);
            return Task.CompletedTask;
        }
    }

    public sealed record SequenceEvent;

    public sealed class SequenceEventOrder : Entity<SequenceEventOrder>, IAmbientExempt
    {
        public int Value { get; set; }
    }

    public sealed class SequenceEventHandler(EventTestState state)
        : IHandleEntityEvent<SequenceEventOrder, SequenceEvent>
    {
        public Task Handle(
            SequenceEventOrder order,
            EventOccurrence<SequenceEvent> occurrence,
            CancellationToken ct)
        {
            state.SequenceObservations.Enqueue(order.Value);
            state.SequenceOccurrenceIds.Enqueue(occurrence.OccurrenceId);
            state.SequenceOrdinals.Enqueue(occurrence.Ordinal);
            return Task.CompletedTask;
        }
    }

    [EventDetailsRequired]
    public sealed record RejectionDetails(string Reason);

    public sealed class RequiredDetailsOrder : Entity<RequiredDetailsOrder>, IAmbientExempt;

    public sealed class RequiredDetailsHandler(EventTestState state)
        : IHandleEntityEvent<RequiredDetailsOrder, RejectionDetails>
    {
        public Task Handle(
            RequiredDetailsOrder order,
            EventOccurrence<RejectionDetails> occurrence,
            CancellationToken ct)
        {
            state.RequiredDetailsObservations.Enqueue(occurrence.Details.Reason);
            return Task.CompletedTask;
        }
    }

    public sealed record OptionalDetails(string Note);

    public sealed class OptionalDetailsOrder : Entity<OptionalDetailsOrder>, IAmbientExempt;

    public sealed class OptionalDetailsHandler(EventTestState state)
        : IHandleEntityEvent<OptionalDetailsOrder, OptionalDetails>
    {
        public Task Handle(
            OptionalDetailsOrder order,
            EventOccurrence<OptionalDetails> occurrence,
            CancellationToken ct)
        {
            state.OptionalDetailsObservations.Enqueue(
                occurrence.HasDetails ? occurrence.Details.Note : "payloadless");
            return Task.CompletedTask;
        }
    }

    public sealed record FilterEvent;

    public sealed class FilterEventOrder : Entity<FilterEventOrder>, IAmbientExempt
    {
        public bool Accepted { get; set; }
    }

    public sealed class FilterEventHandler(EventTestState state)
        : IHandleEntityEvent<FilterEventOrder, FilterEvent>
    {
        public bool Where(FilterEventOrder order, EventOccurrence<FilterEvent> occurrence)
            => order.Accepted;

        public Task Handle(
            FilterEventOrder order,
            EventOccurrence<FilterEvent> occurrence,
            CancellationToken ct)
        {
            Interlocked.Increment(ref state.FilterHandled);
            return Task.CompletedTask;
        }
    }

    public sealed record FailureEvent;

    public sealed class FailureEventOrder : Entity<FailureEventOrder>, IAmbientExempt;

    public sealed class FailureAThrowingHandler : IHandleEntityEvent<FailureEventOrder, FailureEvent>
    {
        public Task Handle(
            FailureEventOrder order,
            EventOccurrence<FailureEvent> occurrence,
            CancellationToken ct) => throw new InvalidOperationException("expected event handler failure");
    }

    public sealed class FailureBSurvivorHandler(EventTestState state)
        : IHandleEntityEvent<FailureEventOrder, FailureEvent>
    {
        public Task Handle(
            FailureEventOrder order,
            EventOccurrence<FailureEvent> occurrence,
            CancellationToken ct)
        {
            Interlocked.Increment(ref state.FailureSurvivorHandled);
            return Task.CompletedTask;
        }
    }

    public sealed record TenantEvent;

    public sealed class TenantEventOrder : Entity<TenantEventOrder>;

    public sealed class TenantEventHandler(EventTestState state)
        : IHandleEntityEvent<TenantEventOrder, TenantEvent>
    {
        public Task Handle(
            TenantEventOrder order,
            EventOccurrence<TenantEvent> occurrence,
            CancellationToken ct)
        {
            state.TenantObservations.Enqueue(Tenant.Current?.Id);
            return Task.CompletedTask;
        }
    }

    public sealed record CancellationEvent;

    public sealed class CancellationEventOrder : Entity<CancellationEventOrder>, IAmbientExempt
    {
        public int Value { get; set; }
    }

    public sealed class CancellationEventHandler(EventTestState state)
        : IHandleEntityEvent<CancellationEventOrder, CancellationEvent>
    {
        public async Task Handle(
            CancellationEventOrder order,
            EventOccurrence<CancellationEvent> occurrence,
            CancellationToken ct)
        {
            if (Interlocked.Increment(ref state.CancellationHandled) == 1)
            {
                state.CancellationStarted.TrySetResult(true);
                await state.CancellationRelease.Task.WaitAsync(ct);
            }
        }
    }

    public sealed record DrainEvent;

    public sealed class DrainEventOrder : Entity<DrainEventOrder>, IAmbientExempt;

    public sealed class DrainEventHandler(EventTestState state)
        : IHandleEntityEvent<DrainEventOrder, DrainEvent>
    {
        public async Task Handle(
            DrainEventOrder order,
            EventOccurrence<DrainEvent> occurrence,
            CancellationToken ct)
        {
            state.DrainStarted.TrySetResult(true);
            await state.DrainRelease.Task.WaitAsync(ct);
            Interlocked.Increment(ref state.DrainHandled);
        }
    }

    public sealed record IsolationEvent;

    public sealed class IsolationEventOrder : Entity<IsolationEventOrder>, IAmbientExempt;

    public sealed class IsolationEventHandler(EventTestState state)
        : IHandleEntityEvent<IsolationEventOrder, IsolationEvent>
    {
        public Task Handle(
            IsolationEventOrder order,
            EventOccurrence<IsolationEvent> occurrence,
            CancellationToken ct)
        {
            Interlocked.Increment(ref state.IsolationHandled);
            return Task.CompletedTask;
        }
    }

    public sealed record NoSubscriberEvent;

    public sealed class NoSubscriberEventOrder : Entity<NoSubscriberEventOrder>, IAmbientExempt;
}
