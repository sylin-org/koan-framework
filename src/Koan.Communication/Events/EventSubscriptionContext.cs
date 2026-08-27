using Koan.Data.Abstractions;

namespace Koan.Communication;

/// <summary>
/// One gateway-registered event handler: an inline lambda that observes a business event on a
/// specific Entity kind. Created through <c>Order.Events.On&lt;OrderShipped&gt;(handler)</c> —
/// the gateway feeds into the same binding pipeline as discovered
/// <c>IHandleEntityEvent&lt;TEntity,TEvent&gt;</c> classes; it is sugar for the common
/// one-handler-one-lambda case, not a parallel mechanism.
/// </summary>
/// <typeparam name="TModel">The Entity kind that raised the event.</typeparam>
/// <typeparam name="TEvent">The event payload type (plain reference type; records by convention).</typeparam>
public sealed class EventSubscriptionContext<TModel, TEvent>
    where TModel : class, IEntity
    where TEvent : class
{
    public required TModel Entity { get; init; }
    public required EventOccurrence<TEvent> Occurrence { get; init; }
    public TEvent? Details => Occurrence.HasDetails ? Occurrence.Details : default;
    public string Justification => Occurrence.HasDetails
        ? Occurrence.Details?.ToString() ?? string.Empty
        : string.Empty;
}
