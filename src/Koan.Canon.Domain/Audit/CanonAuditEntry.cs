using System;
using System.Collections.Generic;

namespace Koan.Canon.Domain.Audit;

/// <summary>
/// Represents a canonical audit record produced by policy evaluation.
/// </summary>
public sealed class CanonAuditEntry
{
    /// <summary>
    /// Gets or sets the canonical identifier.
    /// </summary>
    public string CanonicalId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the canonical model CLR type.
    /// </summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the property name affected by the change.
    /// </summary>
    public string Property { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the previous value (string representation).
    /// </summary>
    public string? PreviousValue { get; set; }

    /// <summary>
    /// Gets or sets the current value (string representation).
    /// </summary>
    public string? CurrentValue { get; set; }

    /// <summary>
    /// Gets or sets the policy applied to compute the value.
    /// </summary>
    public string Policy { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the source key that contributed the update.
    /// </summary>
    public string? Source { get; set; }

    /// <summary>
    /// Gets or sets the arrival token associated with the winning payload.
    /// </summary>
    public string? ArrivalToken { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when the entry was created.
    /// </summary>
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or sets arbitrary evidence for diagnostics.
    /// </summary>
    public Dictionary<string, string?> Evidence { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
