namespace Koan.Communication;

/// <summary>One typed business occurrence delivered to an Entity Event subscription.</summary>
public sealed class EventOccurrence<TEvent>
    where TEvent : class
{
    private readonly TEvent? _details;

    internal EventOccurrence(
        Guid operationId,
        Guid occurrenceId,
        long ordinal,
        DateTimeOffset occurredAt,
        TEvent? details,
        bool hasDetails)
    {
        OperationId = operationId;
        OccurrenceId = occurrenceId;
        Ordinal = ordinal;
        OccurredAt = occurredAt;
        _details = details;
        HasDetails = hasDetails;
    }

    public Guid OperationId { get; }
    public Guid OccurrenceId { get; }
    public long Ordinal { get; }
    public DateTimeOffset OccurredAt { get; }
    public bool HasDetails { get; }

    /// <summary>Gets explicit details, or throws when this is a payloadless occurrence.</summary>
    public TEvent Details => _details ?? throw new InvalidOperationException(
        $"Entity Event '{typeof(TEvent).FullName}' was raised without details.");

    /// <summary>Gets explicit details when present.</summary>
    public TEvent? DetailsOrDefault => _details;
}
