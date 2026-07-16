using Koan.Core.Hosting.Registry;
using Koan.Data.Abstractions;

namespace Koan.Communication.Runtime;

internal sealed class CommunicationHandlerCatalog
{
    private readonly IReadOnlyDictionary<Type, IReadOnlyList<TransportReceiverBinding>> _transportByEntity;
    private readonly IReadOnlyDictionary<(Type EntityType, Type EventType), IReadOnlyList<EventHandlerBinding>>
        _eventsByContract;

    private CommunicationHandlerCatalog(
        IEnumerable<Type> discoveredTransportHandlers,
        IEnumerable<Type> discoveredEventHandlers)
    {
        TransportReceivers = BuildTransportBindings(discoveredTransportHandlers);
        EventSubscriptions = BuildEventBindings(discoveredEventHandlers);
        HandlerTypes = TransportReceivers
            .Select(static binding => binding.HandlerType)
            .Concat(EventSubscriptions.Select(static binding => binding.HandlerType))
            .Distinct()
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .ToArray();
        _transportByEntity = TransportReceivers
            .GroupBy(static binding => binding.EntityType)
            .ToDictionary(
                static group => group.Key,
                static group => (IReadOnlyList<TransportReceiverBinding>)group.ToArray());
        _eventsByContract = EventSubscriptions
            .GroupBy(static binding => (binding.EntityType, binding.EventType))
            .ToDictionary(
                static group => group.Key,
                static group => (IReadOnlyList<EventHandlerBinding>)group.ToArray());
    }

    public IReadOnlyList<TransportReceiverBinding> TransportReceivers { get; }
    public IReadOnlyList<EventHandlerBinding> EventSubscriptions { get; }
    public IReadOnlyList<Type> HandlerTypes { get; }

    public IReadOnlyList<TransportReceiverBinding> TransportFor(Type entityType)
        => _transportByEntity.TryGetValue(entityType, out var bindings) ? bindings : [];

    public IReadOnlyList<EventHandlerBinding> EventsFor(Type entityType, Type eventType)
        => _eventsByContract.TryGetValue((entityType, eventType), out var bindings) ? bindings : [];

    public static CommunicationHandlerCatalog FromDiscovery()
        => new(
            KoanRegistry.GetDiscoveredImplementors(typeof(IReceiveEntity)),
            KoanRegistry.GetDiscoveredImplementors(typeof(IHandleEntityEvent)));

    private static IReadOnlyList<TransportReceiverBinding> BuildTransportBindings(
        IEnumerable<Type> discoveredHandlers)
    {
        var bindings = new List<TransportReceiverBinding>();
        foreach (var handlerType in ValidHandlers(discoveredHandlers, "Transport receiver"))
        {
            var contracts = handlerType.GetInterfaces()
                .Where(static contract => contract.IsGenericType
                                          && contract.GetGenericTypeDefinition() == typeof(IReceiveEntity<>))
                .OrderBy(static contract => contract.GenericTypeArguments[0].FullName, StringComparer.Ordinal)
                .ToArray();
            if (contracts.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Transport receiver '{handlerType.FullName}' implements IReceiveEntity without " +
                    "an IReceiveEntity<TEntity> contract.");
            }

            foreach (var contract in contracts)
            {
                var entityType = contract.GenericTypeArguments[0];
                EnsureEntity(entityType, handlerType, "Transport receiver");
                bindings.Add(TransportReceiverBinding.Create(entityType, handlerType));
            }
        }

        return bindings
            .OrderBy(static binding => binding.EntityType.FullName, StringComparer.Ordinal)
            .ThenBy(static binding => binding.GroupIdentity, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<EventHandlerBinding> BuildEventBindings(
        IEnumerable<Type> discoveredHandlers)
    {
        var bindings = new List<EventHandlerBinding>();
        foreach (var handlerType in ValidHandlers(discoveredHandlers, "Event handler"))
        {
            var contracts = handlerType.GetInterfaces()
                .Where(static contract => contract.IsGenericType
                                          && contract.GetGenericTypeDefinition() == typeof(IHandleEntityEvent<,>))
                .OrderBy(static contract => contract.GenericTypeArguments[0].FullName, StringComparer.Ordinal)
                .ThenBy(static contract => contract.GenericTypeArguments[1].FullName, StringComparer.Ordinal)
                .ToArray();
            if (contracts.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Event handler '{handlerType.FullName}' implements IHandleEntityEvent without " +
                    "an IHandleEntityEvent<TEntity, TEvent> contract.");
            }

            foreach (var contract in contracts)
            {
                var entityType = contract.GenericTypeArguments[0];
                var eventType = contract.GenericTypeArguments[1];
                EnsureEntity(entityType, handlerType, "Event handler");
                if (!eventType.IsClass)
                {
                    throw new InvalidOperationException(
                        $"Event handler '{handlerType.FullName}' targets '{eventType.FullName}', " +
                        "which is not a reference-type Event contract.");
                }

                bindings.Add(EventHandlerBinding.Create(entityType, eventType, handlerType));
            }
        }

        return bindings
            .OrderBy(static binding => binding.EntityType.FullName, StringComparer.Ordinal)
            .ThenBy(static binding => binding.EventType.FullName, StringComparer.Ordinal)
            .ThenBy(static binding => binding.GroupIdentity, StringComparer.Ordinal)
            .ToArray();
    }

    private static IEnumerable<Type> ValidHandlers(IEnumerable<Type> discoveredHandlers, string role)
    {
        foreach (var handlerType in discoveredHandlers
                     .Distinct()
                     .OrderBy(static type => type.FullName, StringComparer.Ordinal))
        {
            if (handlerType is not { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false })
            {
                throw new InvalidOperationException(
                    $"{role} '{handlerType.FullName}' must be a concrete, closed class.");
            }

            yield return handlerType;
        }
    }

    private static void EnsureEntity(Type entityType, Type handlerType, string role)
    {
        if (!typeof(IEntity).IsAssignableFrom(entityType))
        {
            throw new InvalidOperationException(
                $"{role} '{handlerType.FullName}' targets '{entityType.FullName}', " +
                "which is not a Koan Entity.");
        }
    }
}
