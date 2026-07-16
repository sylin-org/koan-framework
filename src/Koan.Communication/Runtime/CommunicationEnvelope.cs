namespace Koan.Communication.Runtime;

internal abstract class CommunicationEnvelope(
    Adapters.CommunicationLane lane,
    Guid operationId,
    long ordinal,
    Type entityType,
    string entityPayload,
    IReadOnlyDictionary<string, string>? context)
{
    public Adapters.CommunicationLane Lane { get; } = lane;
    public Guid OperationId { get; } = operationId;
    public long Ordinal { get; } = ordinal;
    public Type EntityType { get; } = entityType;
    public string EntityPayload { get; } = entityPayload;
    public IReadOnlyDictionary<string, string>? Context { get; } = context;
}
