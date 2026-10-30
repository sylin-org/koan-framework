namespace Koan.Communication.Tests.Support;

/// <summary>
/// An external provider whose transport carries at most <paramref name="maxPayloadBytes"/> per publication. Pin it with
/// <c>TransportProvider</c> or <c>EventsProvider = "limited"</c>.
/// </summary>
public sealed class LimitedAdapter(int maxPayloadBytes) : ICommunicationAdapter
{
    public CommunicationAdapterDescriptor Descriptor { get; } = new(
        "limited",
        [CommunicationLane.Transport, CommunicationLane.Events],
        CommunicationDeliveryAssurance.Acknowledged,
        CommunicationAdapterCapabilities.ContractIdentity
        | CommunicationAdapterCapabilities.SnapshotCopy
        | CommunicationAdapterCapabilities.ContextCarriage
        | CommunicationAdapterCapabilities.TypedGroups
        | CommunicationAdapterCapabilities.GroupFanOut
        | CommunicationAdapterCapabilities.MessageIdentity
        | CommunicationAdapterCapabilities.BoundedAcceptance
        | CommunicationAdapterCapabilities.ZeroTargetEvents,
        ["Sylin.Koan.Communication.Connector.Limited"],
        IngressTrust: Koan.Core.Context.ContextIngressTrust.Authenticated,
        MaxPayloadBytes: maxPayloadBytes);

    public int PublishCount;
    public bool IsReady { get; private set; }

    public Task Start(CommunicationAdapterHost host, CancellationToken ct)
    {
        IsReady = true;
        return Task.CompletedTask;
    }

    public ValueTask<CommunicationAdapterAcceptance> Publish(
        CommunicationAdapterPublication publication,
        CancellationToken ct)
    {
        Interlocked.Increment(ref PublishCount);
        return ValueTask.FromResult(new CommunicationAdapterAcceptance(null, SettlementObservable: false));
    }

    public Task Stop(CancellationToken ct)
    {
        IsReady = false;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
