using Koan.Communication.Adapters;

namespace Koan.Communication.Runtime;

internal sealed record CommunicationRouteDecision(
    CommunicationLane Lane,
    string Channel,
    ICommunicationAdapter Adapter,
    string Reason,
    bool DirectIntent,
    int Priority)
{
    public string AdapterId => Adapter.Descriptor.Id;
    public string Assurance => Adapter.Descriptor.Assurance switch
    {
        CommunicationDeliveryAssurance.ProcessMemory => "process-memory",
        CommunicationDeliveryAssurance.BestEffort => "best-effort",
        CommunicationDeliveryAssurance.Acknowledged => "acknowledged",
        CommunicationDeliveryAssurance.DurablyAcknowledged => "durably-acknowledged",
        _ => "unknown"
    };
}
