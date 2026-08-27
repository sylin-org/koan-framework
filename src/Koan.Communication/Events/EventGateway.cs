using System.Collections.Concurrent;
using Koan.Data.Abstractions;

namespace Koan.Communication;

/// <summary>
/// The type-scoped event gateway for one Entity kind. Lambda-based handlers registered here feed
/// into the same binding pipeline as discovered <c>IHandleEntityEvent&lt;TEntity,TEvent&gt;</c>
/// classes — this is sugar for the common one-handler-one-lambda case, not a parallel mechanism.
/// Present whenever <c>Sylin.Koan.Communication</c> is referenced.
/// </summary>
public readonly struct EventGateway<TModel>
    where TModel : class, IEntity
{
    /// <summary>
    /// Register a handler for one event type on this Entity kind. The handler receives the Entity
    /// snapshot, the occurrence metadata, and the strongly-typed event details. A null return from
    /// the filter function filters the occurrence (same semantics as <c>IHandleEntityEvent.Where</c>).
    /// Must be called before the host starts (composition time), so the binding pipeline sees it.
    /// </summary>
    public EventGateway<TModel> On<TEvent>(
        Func<TModel, EventOccurrence<TEvent>, CancellationToken, Task> handler,
        Func<TModel, EventOccurrence<TEvent>, bool>? where = null)
        where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(handler);
        EventSubscriptionRegistry.Add<TModel, TEvent>(handler, where);
        return this;
    }

    /// <summary>Remove every gateway-registered handler for this Entity kind. Intended for test isolation.</summary>
    public EventGateway<TModel> Reset()
    {
        EventSubscriptionRegistry.Reset<TModel>();
        return this;
    }
}

/// <summary>
/// Process-global per-Entity-kind registry of gateway-registered event handlers. Consumed by
/// <c>CommunicationHandlerCatalog</c> at composition time to produce lambda bindings that enter
/// the same binding pipeline as discovered handler classes.
/// </summary>
internal static class EventSubscriptionRegistry
{
    private static readonly ConcurrentDictionary<(Type Model, Type Event), List<object>> Handlers = new();

    public static void Add<TModel, TEvent>(
        Func<TModel, EventOccurrence<TEvent>, CancellationToken, Task> handler,
        Func<TModel, EventOccurrence<TEvent>, bool>? where)
        where TModel : class, IEntity
        where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(handler);
        var key = (typeof(TModel), typeof(TEvent));
        var list = Handlers.GetOrAdd(key, static _ => new List<object>());
        lock (list)
        {
            list.Add((handler, where));
        }
    }

    public static IReadOnlyList<(Func<TModel, EventOccurrence<TEvent>, CancellationToken, Task>, Func<TModel, EventOccurrence<TEvent>, bool>?)>
        Get<TModel, TEvent>()
        where TModel : class, IEntity
        where TEvent : class
    {
        if (!Handlers.TryGetValue((typeof(TModel), typeof(TEvent)), out var list)) return [];
        lock (list)
        {
            return list.Select(h => ((Func<TModel, EventOccurrence<TEvent>, CancellationToken, Task>, Func<TModel, EventOccurrence<TEvent>, bool>?))h).ToArray();
        }
    }

    public static IEnumerable<(Type Model, Type Event)> Registrations =>
        Handlers.Keys.Select(k => (k.Model, k.Event));

    public static void Reset<TModel>()
    {
        foreach (var key in Handlers.Keys.Where(k => k.Model == typeof(TModel)).ToList())
            Handlers.TryRemove(key, out _);
    }
}

