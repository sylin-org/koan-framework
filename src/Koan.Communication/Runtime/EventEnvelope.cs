namespace Koan.Communication.Runtime;

internal sealed class EventEnvelope(
    Guid operationId,
    long ordinal,
    Type entityType,
    string entityPayload,
    IReadOnlyDictionary<string, string>? context,
    Type eventType,
    Guid occurrenceId,
    DateTimeOffset occurredAt,
    bool hasDetails,
    string? detailsPayload)
    : CommunicationEnvelope(
        Adapters.CommunicationLane.Events,
        operationId,
        ordinal,
        entityType,
        entityPayload,
        context)
{
    public Type EventType { get; } = eventType;
    public Guid OccurrenceId { get; } = occurrenceId;
    public DateTimeOffset OccurredAt { get; } = occurredAt;
    public bool HasDetails { get; } = hasDetails;
    public string? DetailsPayload { get; } = detailsPayload;
}
