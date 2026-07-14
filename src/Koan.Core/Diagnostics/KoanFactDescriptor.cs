namespace Koan.Core.Diagnostics;

/// <summary>
/// Provider-neutral input accepted from Koan modules. The host recorder performs normalization,
/// redaction, stable identity, ordering, and schema projection; arbitrary provider payloads are not
/// accepted.
/// </summary>
public sealed record KoanFactDescriptor(
    string Code,
    KoanFactKind Kind,
    KoanFactState State,
    string Subject,
    string Summary,
    string ReasonCode,
    string? Correction,
    string Source,
    string CorrelationId,
    DateTimeOffset? ObservedAtUtc = null);
