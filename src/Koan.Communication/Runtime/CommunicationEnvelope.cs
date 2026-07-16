namespace Koan.Communication.Runtime;

internal abstract class CommunicationEnvelope(
    Adapters.CommunicationLane lane,
    Guid operationId,
    long ordinal,
    Type contractType,
    string payload,
    IReadOnlyDictionary<string, string>? context)
{
    public Adapters.CommunicationLane Lane { get; } = lane;
    public Guid OperationId { get; } = operationId;
    public long Ordinal { get; } = ordinal;
    public Type ContractType { get; } = contractType;
    public string Payload { get; } = payload;
    public IReadOnlyDictionary<string, string>? Context { get; } = context;
}
