namespace Sora.Messaging;

public sealed record MessageEnvelope(
    string Id,
    string TypeAlias,
    string? CorrelationId,
    string? CausationId,
    IReadOnlyDictionary<string, string> Headers,
    int Attempt,
    DateTimeOffset Timestamp
);

// Message metadata attributes

// Generic batch message wrapper for grouped handling