using Koan.Core.Context;

namespace Koan.Communication.Adapters;

/// <summary>The semantic lanes a Communication adapter can faithfully carry.</summary>
public enum CommunicationLane
{
    Events,
    Transport,
    /// <summary>
    /// Framework-owned, non-application signals such as a Jobs wake hint. This infrastructure lane does not
    /// add an arbitrary-object Messaging surface to Entity or application code.
    /// </summary>
    FrameworkSignals,
    /// <summary>
    /// Framework-owned signals delivered once to every active node within the elected provider's reach.
    /// This differs from <see cref="FrameworkSignals"/>, whose stable receiver groups may compete across replicas.
    /// </summary>
    FrameworkBroadcasts
}

/// <summary>The point at which an adapter reports publication acceptance.</summary>
public enum CommunicationDeliveryAssurance
{
    ProcessMemory = 0,
    BestEffort = 1,
    Acknowledged = 2,
    DurablyAcknowledged = 3
}

/// <summary>Hard semantic invariants declared by a Communication adapter and enforced during election.</summary>
[Flags]
public enum CommunicationAdapterCapabilities
{
    None = 0,
    ContractIdentity = 1 << 0,
    SnapshotCopy = 1 << 1,
    ContextCarriage = 1 << 2,
    TypedGroups = 1 << 3,
    GroupFanOut = 1 << 4,
    MessageIdentity = 1 << 5,
    BoundedAcceptance = 1 << 6,
    ZeroTargetEvents = 1 << 7,
    NodeFanOut = 1 << 8
}

/// <summary>The lifetime and fan-out identity represented by one receiver binding.</summary>
public enum CommunicationBindingScope
{
    /// <summary>Replicas with the same group identity compete for each publication.</summary>
    ConsumerGroup,
    /// <summary>Each active node owns an ephemeral binding and receives its own copy.</summary>
    Node
}

/// <summary>
/// An immutable, side-effect-free declaration of one Communication provider candidate. A layered candidate may
/// replace the built-in floor when its owning engine activates it; declaring zero lanes keeps it dormant.
/// </summary>
public sealed record CommunicationAdapterDescriptor(
    string Id,
    IReadOnlyList<CommunicationLane> Lanes,
    CommunicationDeliveryAssurance Assurance,
    CommunicationAdapterCapabilities Capabilities,
    IReadOnlyList<string> DirectReferenceIdentities,
    bool IsBuiltIn = false,
    bool IsLayered = false);

/// <summary>One stable local receiver/subscription group that an elected adapter must bind once.</summary>
public sealed record CommunicationAdapterBinding(
    string Id,
    CommunicationLane Lane,
    string Channel,
    string ContractId,
    string GroupId,
    CommunicationBindingScope Scope = CommunicationBindingScope.ConsumerGroup);

/// <summary>One host-encoded publication. Adapters route the bytes but never interpret Entity or context axes.</summary>
public sealed class CommunicationAdapterPublication
{
    internal CommunicationAdapterPublication(
        CommunicationLane lane,
        string channel,
        string contractId,
        string messageId,
        ReadOnlyMemory<byte> payload,
        Runtime.CommunicationOperation operation)
    {
        Lane = lane;
        Channel = channel;
        ContractId = contractId;
        MessageId = messageId;
        Payload = payload;
        Operation = operation;
    }

    public CommunicationLane Lane { get; }
    public string Channel { get; }
    public string ContractId { get; }
    public string MessageId { get; }
    public ReadOnlyMemory<byte> Payload { get; }
    internal Runtime.CommunicationOperation Operation { get; }
}

/// <summary>What an adapter knows at the publisher boundary after accepting one item.</summary>
public sealed record CommunicationAdapterAcceptance(
    int? TargetGroups,
    bool SettlementObservable);

/// <summary>The terminal result of one local inbound group invocation.</summary>
public enum CommunicationDeliveryOutcome
{
    Delivered,
    Filtered,
    Failed
}

/// <summary>Host services made available to an elected adapter without exposing business handlers.</summary>
public sealed class CommunicationAdapterHost
{
    private readonly Func<string, ReadOnlyMemory<byte>, ContextIngressTrust, CancellationToken,
        Task<CommunicationDeliveryOutcome>> _dispatch;

    internal CommunicationAdapterHost(
        string meshId,
        IReadOnlyList<CommunicationAdapterBinding> bindings,
        Func<string, ReadOnlyMemory<byte>, ContextIngressTrust, CancellationToken,
            Task<CommunicationDeliveryOutcome>> dispatch)
    {
        MeshId = meshId;
        Bindings = bindings;
        _dispatch = dispatch;
    }

    public string MeshId { get; }
    public IReadOnlyList<CommunicationAdapterBinding> Bindings { get; }

    public Task<CommunicationDeliveryOutcome> Dispatch(
        string bindingId,
        ReadOnlyMemory<byte> payload,
        ContextIngressTrust ingressTrust,
        CancellationToken ct = default)
        => _dispatch(bindingId, payload, ingressTrust, ct);
}

/// <summary>Infrastructure-facing connector seam. Business applications use Entity Events and Transport instead.</summary>
public interface ICommunicationAdapter : IAsyncDisposable
{
    CommunicationAdapterDescriptor Descriptor { get; }
    bool IsReady { get; }
    Task Start(CommunicationAdapterHost host, CancellationToken ct);
    ValueTask<CommunicationAdapterAcceptance> Publish(
        CommunicationAdapterPublication publication,
        CancellationToken ct);
    Task Stop(CancellationToken ct);
}

/// <summary>A provider-boundary failure that the Communication router translates into lane-owned errors.</summary>
public sealed class CommunicationAdapterException : Exception
{
    public CommunicationAdapterException(
        FailureKind failure,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
        => Failure = failure;

    public FailureKind Failure { get; }

    public enum FailureKind
    {
        NoRoute,
        Unavailable
    }
}
