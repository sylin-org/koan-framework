using Microsoft.Extensions.DependencyInjection;

namespace Koan.Communication.Runtime;

internal abstract class CommunicationTargetBinding
{
    protected CommunicationTargetBinding(
        Type contractType,
        Type handlerType,
        string groupIdentity)
    {
        ContractType = contractType;
        HandlerType = handlerType;
        GroupIdentity = groupIdentity;
    }

    public Type ContractType { get; }
    public Type HandlerType { get; }
    public string GroupIdentity { get; }

    public abstract Task<CommunicationTargetOutcome> Dispatch(
        IServiceProvider services,
        CommunicationEnvelope envelope,
        CancellationToken ct);

    protected object ResolveHandler(IServiceProvider services)
        => services.GetRequiredService(HandlerType);
}
