namespace Koan.Communication.Runtime;

internal sealed class FrameworkSignalEnvelope(
    Guid operationId,
    Type signalType,
    string payload)
    : CommunicationEnvelope(
        Adapters.CommunicationLane.FrameworkSignals,
        operationId,
        ordinal: 0,
        signalType,
        payload,
        context: null);
