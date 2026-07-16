using Microsoft.Extensions.DependencyInjection;

namespace Koan.Communication.Runtime;

internal abstract class CommunicationTargetBinding
{
    protected CommunicationTargetBinding(
        Type entityType,
        Type handlerType,
        string groupIdentity)
    {
        EntityType = entityType;
        HandlerType = handlerType;
        GroupIdentity = groupIdentity;
    }

    public Type EntityType { get; }
    public Type HandlerType { get; }
    public string GroupIdentity { get; }

    public abstract Task<CommunicationTargetOutcome> Dispatch(
        IServiceProvider services,
        CommunicationEnvelope envelope,
        CancellationToken ct);

    protected object ResolveHandler(IServiceProvider services)
        => services.GetRequiredService(HandlerType);
}
