namespace Koan.Communication.Runtime;

internal abstract class CommunicationEnvelope(
    CommunicationLane lane,
    CommunicationOperation operation,
    long ordinal,
    Type entityType,
    string entityPayload,
    IReadOnlyDictionary<string, string>? context,
    IReadOnlyList<CommunicationTargetBinding> targets)
{
    public CommunicationLane Lane { get; } = lane;
    public CommunicationOperation Operation { get; } = operation;
    public long Ordinal { get; } = ordinal;
    public Type EntityType { get; } = entityType;
    public string EntityPayload { get; } = entityPayload;
    public IReadOnlyDictionary<string, string>? Context { get; } = context;
    public IReadOnlyList<CommunicationTargetBinding> Targets { get; } = targets;
}
