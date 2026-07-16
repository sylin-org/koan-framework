namespace Koan.Communication.Runtime;

internal sealed class FrameworkSignalEnvelope(
    Guid operationId,
    Adapters.CommunicationLane lane,
    Type signalType,
    string payload)
    : CommunicationEnvelope(
        lane,
        operationId,
        ordinal: 0,
        signalType,
        payload,
        context: null);
