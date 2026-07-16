namespace Koan.Communication.Runtime;

internal sealed record TransportEnvelope(
    TransportOperation Operation,
    long Ordinal,
    Type EntityType,
    string Payload,
    IReadOnlyDictionary<string, string>? Context,
    IReadOnlyList<TransportReceiverBinding> Receivers);
