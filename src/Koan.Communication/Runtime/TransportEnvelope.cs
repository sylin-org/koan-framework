namespace Koan.Communication.Runtime;

internal sealed class TransportEnvelope(
    Guid operationId,
    long ordinal,
    Type entityType,
    string entityPayload,
    IReadOnlyDictionary<string, string>? context)
    : CommunicationEnvelope(
        Adapters.CommunicationLane.Transport,
        operationId,
        ordinal,
        entityType,
        entityPayload,
        context);
