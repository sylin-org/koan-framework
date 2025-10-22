using System;
using System.Collections.Generic;

namespace Koan.Canon.Domain.Metadata;

/// <summary>
/// Tracks provenance information for a single canonical property value.
/// </summary>
public sealed class CanonPropertyFootprint
{
    private Dictionary<string, string?> _evidence = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the property name.
    /// </summary>
    public string Property { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the source key (integration or contributor) responsible for the value.
    /// </summary>
    public string? SourceKey { get; set; }

    /// <summary>
    /// Gets or sets the arrival token used to order competing payloads.
    /// </summary>
    public string? ArrivalToken { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when the value was accepted.
    /// </summary>
    public DateTimeOffset ArrivedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or sets the serialized representation of the last accepted value.
    /// </summary>
    public string? Value { get; set; }

    /// <summary>
    /// Gets or sets the policy kind that produced the value.
    /// </summary>
    public string? Policy { get; set; }

    /// <summary>
    /// Gets or sets optional evidence about the decision (key/value pairs).
    /// </summary>
    public Dictionary<string, string?> Evidence
    {
        get => _evidence;
        set => _evidence = value is null
            ? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string?>(value, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Creates a deep copy of the footprint.
    /// </summary>
    public CanonPropertyFootprint Clone()
    {
        return new CanonPropertyFootprint
        {
            Property = Property,
            SourceKey = SourceKey,
            ArrivalToken = ArrivalToken,
            ArrivedAt = ArrivedAt,
            Value = Value,
            Policy = Policy,
            Evidence = new Dictionary<string, string?>(_evidence, StringComparer.OrdinalIgnoreCase)
        };
    }
}
