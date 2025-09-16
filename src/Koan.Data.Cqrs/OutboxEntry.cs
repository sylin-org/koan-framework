namespace Koan.Data.Cqrs;

/// <summary>
/// Outbox entry used by implicit CQRS; generic envelope to avoid user code.
/// </summary>
public sealed record OutboxEntry(
    string Id,
    DateTimeOffset OccurredAt,
    string EntityType,
    string Operation, // Upsert/Delete
    string EntityId,
    string PayloadJson,
    string? CorrelationId = null,
    string? CausationId = null
);