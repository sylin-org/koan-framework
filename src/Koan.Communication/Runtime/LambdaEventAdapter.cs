using Koan.Data.Abstractions;

namespace Koan.Communication;

/// <summary>
/// PMC-056: synthetic event handler adapter for gateway-registered lambdas. Enters the binding
/// pipeline as a normal <see cref="IHandleEntityEvent{TEntity, TEvent}"/>, dispatching through
/// <see cref="EventSubscriptionRegistry"/> at runtime. One closed adapter per (Entity, Event)
/// pair; the adapter delegates to every lambda registered for that pair. Individual lambda
/// filters are evaluated in Handle (not Where), so a filtered lambda does not suppress other
/// lambdas on the same event.
/// </summary>
internal sealed class LambdaEventAdapter<TEntity, TEvent> : IHandleEntityEvent<TEntity, TEvent>
    where TEntity : class, IEntity
    where TEvent : class
{
    public bool Where(TEntity entity, EventOccurrence<TEvent> occurrence) => true;

    public async Task Handle(TEntity entity, EventOccurrence<TEvent> occurrence, CancellationToken ct)
    {
        var handlers = EventSubscriptionRegistry.Get<TEntity, TEvent>();
        foreach (var (handler, filter) in handlers)
        {
            if (filter is { } f && !f(entity, occurrence)) continue;
            await handler(entity, occurrence, ct).ConfigureAwait(false);
        }
    }
}
