namespace Koan.Communication.Runtime;

internal sealed class TransportEnvelope(
    CommunicationOperation operation,
    long ordinal,
    Type entityType,
    string entityPayload,
    IReadOnlyDictionary<string, string>? context,
    IReadOnlyList<CommunicationTargetBinding> targets)
    : CommunicationEnvelope(
        CommunicationLane.Transport,
        operation,
        ordinal,
        entityType,
        entityPayload,
        context,
        targets);
