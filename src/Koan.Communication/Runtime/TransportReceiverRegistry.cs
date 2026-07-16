using Koan.Core.Hosting.Registry;
using Koan.Data.Abstractions;

namespace Koan.Communication.Runtime;

internal sealed class TransportReceiverRegistry
{
    private readonly IReadOnlyDictionary<Type, IReadOnlyList<TransportReceiverBinding>> _byEntity;

    private TransportReceiverRegistry(IEnumerable<Type> discoveredHandlers)
    {
        var bindings = new List<TransportReceiverBinding>();
        var handlerTypes = new HashSet<Type>();

        foreach (var handlerType in discoveredHandlers
                     .Distinct()
                     .OrderBy(static type => type.FullName, StringComparer.Ordinal))
        {
            if (handlerType is not { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false })
            {
                throw new InvalidOperationException(
                    $"Transport receiver '{handlerType.FullName}' must be a concrete, closed class.");
            }

            var entityContracts = handlerType.GetInterfaces()
                .Where(static contract => contract.IsGenericType
                                          && contract.GetGenericTypeDefinition() == typeof(IReceiveEntity<>))
                .OrderBy(static contract => contract.GenericTypeArguments[0].FullName, StringComparer.Ordinal)
                .ToArray();
            if (entityContracts.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Transport receiver '{handlerType.FullName}' implements IReceiveEntity without " +
                    "an IReceiveEntity<TEntity> contract.");
            }

            foreach (var contract in entityContracts)
            {
                var entityType = contract.GenericTypeArguments[0];
                if (!typeof(IEntity).IsAssignableFrom(entityType))
                {
                    throw new InvalidOperationException(
                        $"Transport receiver '{handlerType.FullName}' targets '{entityType.FullName}', " +
                        "which is not a Koan Entity.");
                }

                bindings.Add(TransportReceiverBinding.Create(entityType, handlerType));
            }

            handlerTypes.Add(handlerType);
        }

        All = bindings
            .OrderBy(static binding => binding.EntityType.FullName, StringComparer.Ordinal)
            .ThenBy(static binding => binding.GroupIdentity, StringComparer.Ordinal)
            .ToArray();
        HandlerTypes = handlerTypes.OrderBy(static type => type.FullName, StringComparer.Ordinal).ToArray();
        _byEntity = All
            .GroupBy(static binding => binding.EntityType)
            .ToDictionary(
                static group => group.Key,
                static group => (IReadOnlyList<TransportReceiverBinding>)group.ToArray());
    }

    public IReadOnlyList<TransportReceiverBinding> All { get; }
    public IReadOnlyList<Type> HandlerTypes { get; }

    public IReadOnlyList<TransportReceiverBinding> For(Type entityType)
        => _byEntity.TryGetValue(entityType, out var bindings) ? bindings : [];

    public static TransportReceiverRegistry FromDiscovery()
        => new(KoanRegistry.GetDiscoveredImplementors(typeof(IReceiveEntity)));
}
