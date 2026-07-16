using Koan.Core.Json;
using Koan.Data.Abstractions;

namespace Koan.Communication.Runtime;

internal abstract class TransportReceiverBinding : CommunicationTargetBinding
{
    protected TransportReceiverBinding(Type entityType, Type handlerType)
        : base(
            entityType,
            handlerType,
            $"{handlerType.FullName ?? handlerType.Name}<{entityType.FullName ?? entityType.Name}>")
    {
    }

    public static TransportReceiverBinding Create(Type entityType, Type handlerType)
    {
        var closed = typeof(Bound<>).MakeGenericType(entityType);
        return (TransportReceiverBinding)Activator.CreateInstance(closed, handlerType)!;
    }

    private sealed class Bound<TEntity>(Type handlerType)
        : TransportReceiverBinding(typeof(TEntity), handlerType)
        where TEntity : class, IEntity
    {
        public override async Task<CommunicationTargetOutcome> Dispatch(
            IServiceProvider services,
            CommunicationEnvelope envelope,
            CancellationToken ct)
        {
            if (envelope is not TransportEnvelope)
            {
                throw new InvalidOperationException(
                    $"Transport receiver '{HandlerType.FullName}' received a non-Transport envelope.");
            }

            var entity = envelope.EntityPayload.FromJson<TEntity>()
                ?? throw new InvalidOperationException(
                    $"The Transport snapshot for Entity '{typeof(TEntity).FullName}' deserialized to null.");
            var handler = (IReceiveEntity<TEntity>)ResolveHandler(services);
            if (!handler.Where(entity))
            {
                return CommunicationTargetOutcome.Filtered;
            }

            await handler.Receive(entity, ct).ConfigureAwait(false);
            return CommunicationTargetOutcome.Delivered;
        }
    }
}
