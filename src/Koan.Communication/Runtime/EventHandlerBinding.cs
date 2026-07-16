using Koan.Core.Json;
using Koan.Data.Abstractions;

namespace Koan.Communication.Runtime;

internal abstract class EventHandlerBinding : CommunicationTargetBinding
{
    protected EventHandlerBinding(Type entityType, Type eventType, Type handlerType)
        : base(
            entityType,
            handlerType,
            $"{handlerType.FullName ?? handlerType.Name}" +
            $"<{entityType.FullName ?? entityType.Name},{eventType.FullName ?? eventType.Name}>")
        => EventType = eventType;

    public Type EventType { get; }

    public static EventHandlerBinding Create(Type entityType, Type eventType, Type handlerType)
    {
        var closed = typeof(Bound<,>).MakeGenericType(entityType, eventType);
        return (EventHandlerBinding)Activator.CreateInstance(closed, handlerType)!;
    }

    private sealed class Bound<TEntity, TEvent>(Type handlerType)
        : EventHandlerBinding(typeof(TEntity), typeof(TEvent), handlerType)
        where TEntity : class, IEntity
        where TEvent : class
    {
        public override async Task<CommunicationTargetOutcome> Dispatch(
            IServiceProvider services,
            CommunicationEnvelope envelope,
            CancellationToken ct)
        {
            if (envelope is not EventEnvelope eventEnvelope
                || eventEnvelope.EventType != typeof(TEvent))
            {
                throw new InvalidOperationException(
                    $"Event handler '{HandlerType.FullName}' received an incompatible occurrence envelope.");
            }

            var entity = eventEnvelope.EntityPayload.FromJson<TEntity>()
                ?? throw new InvalidOperationException(
                    $"The Event snapshot for Entity '{typeof(TEntity).FullName}' deserialized to null.");
            TEvent? details = null;
            if (eventEnvelope.HasDetails)
            {
                details = eventEnvelope.DetailsPayload?.FromJson<TEvent>()
                    ?? throw new InvalidOperationException(
                        $"The Event details for '{typeof(TEvent).FullName}' deserialized to null.");
            }

            var occurrence = new EventOccurrence<TEvent>(
                eventEnvelope.Operation.OperationId,
                eventEnvelope.OccurrenceId,
                eventEnvelope.Ordinal,
                eventEnvelope.OccurredAt,
                details,
                eventEnvelope.HasDetails);
            var handler = (IHandleEntityEvent<TEntity, TEvent>)ResolveHandler(services);
            if (!handler.Where(entity, occurrence))
            {
                return CommunicationTargetOutcome.Filtered;
            }

            await handler.Handle(entity, occurrence, ct).ConfigureAwait(false);
            return CommunicationTargetOutcome.Delivered;
        }
    }
}
