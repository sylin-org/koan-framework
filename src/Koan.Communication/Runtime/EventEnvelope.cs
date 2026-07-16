namespace Koan.Communication.Runtime;

internal sealed class EventEnvelope(
    CommunicationOperation operation,
    long ordinal,
    Type entityType,
    string entityPayload,
    IReadOnlyDictionary<string, string>? context,
    IReadOnlyList<CommunicationTargetBinding> targets,
    Type eventType,
    Guid occurrenceId,
    DateTimeOffset occurredAt,
    bool hasDetails,
    string? detailsPayload)
    : CommunicationEnvelope(
        CommunicationLane.Events,
        operation,
        ordinal,
        entityType,
        entityPayload,
        context,
        targets)
{
    public Type EventType { get; } = eventType;
    public Guid OccurrenceId { get; } = occurrenceId;
    public DateTimeOffset OccurredAt { get; } = occurredAt;
    public bool HasDetails { get; } = hasDetails;
    public string? DetailsPayload { get; } = detailsPayload;
}
