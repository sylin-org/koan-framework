using Koan.Core.Json;
using Koan.Data.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Communication.Runtime;

internal abstract class TransportReceiverBinding
{
    protected TransportReceiverBinding(Type entityType, Type handlerType)
    {
        EntityType = entityType;
        HandlerType = handlerType;
        GroupIdentity = $"{handlerType.FullName ?? handlerType.Name}<{entityType.FullName ?? entityType.Name}>";
    }

    public Type EntityType { get; }
    public Type HandlerType { get; }
    public string GroupIdentity { get; }

    public abstract Task<TransportTargetOutcome> Dispatch(
        IServiceProvider services,
        string payload,
        CancellationToken ct);

    public static TransportReceiverBinding Create(Type entityType, Type handlerType)
    {
        var closed = typeof(Bound<>).MakeGenericType(entityType);
        return (TransportReceiverBinding)Activator.CreateInstance(closed, handlerType)!;
    }

    private sealed class Bound<TEntity>(Type handlerType)
        : TransportReceiverBinding(typeof(TEntity), handlerType)
        where TEntity : class, IEntity
    {
        public override async Task<TransportTargetOutcome> Dispatch(
            IServiceProvider services,
            string payload,
            CancellationToken ct)
        {
            var entity = payload.FromJson<TEntity>()
                ?? throw new InvalidOperationException(
                    $"The Transport snapshot for Entity '{typeof(TEntity).FullName}' deserialized to null.");
            var handler = (IReceiveEntity<TEntity>)services.GetRequiredService(HandlerType);
            if (!handler.Where(entity))
            {
                return TransportTargetOutcome.Filtered;
            }

            await handler.Receive(entity, ct).ConfigureAwait(false);
            return TransportTargetOutcome.Delivered;
        }
    }
}
