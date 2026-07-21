using Koan.Communication.Adapters;
using Koan.Core.Providers;
using Koan.Core.Context;

namespace Koan.Communication.Runtime;

internal sealed record CommunicationRouteDecision(
    CommunicationLane Lane,
    string Channel,
    ICommunicationAdapter Adapter,
    ProviderSelectionReceipt Receipt)
{
    public string AdapterId => Receipt.ProviderId;
    public string Reason => Receipt.Reason;
    public bool DirectIntent => Receipt.DirectIntent;
    public int Priority => Receipt.Priority;
    public string Assurance => Adapter.Descriptor.Assurance switch
    {
        CommunicationDeliveryAssurance.ProcessMemory => "process-memory",
        CommunicationDeliveryAssurance.BestEffort => "best-effort",
        CommunicationDeliveryAssurance.Acknowledged => "acknowledged",
        CommunicationDeliveryAssurance.DurablyAcknowledged => "durably-acknowledged",
        _ => "unknown"
    };

    public string IngressTrust => Adapter.Descriptor.IngressTrust switch
    {
        ContextIngressTrust.Unverified => "unverified",
        ContextIngressTrust.Authenticated => "authenticated",
        ContextIngressTrust.HostTrusted => "host-trusted",
        _ => "unknown"
    };
}
