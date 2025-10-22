namespace Koan.Canon.Domain.Metadata;

/// <summary>
/// Represents an external identifier captured during canonization.
/// </summary>
public sealed class CanonExternalId
{
    /// <summary>
    /// Identifier scheme (e.g., crm, sap, device).
    /// </summary>
    public string Scheme { get; set; } = string.Empty;

    /// <summary>
    /// Identifier value.
    /// </summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// Optional source attribution key.
    /// </summary>
    public string? Source { get; set; }

    /// <summary>
    /// Timestamp when the identifier was observed.
    /// </summary>
    public DateTimeOffset ObservedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Additional attributes supplied by pipeline steps.
    /// </summary>
    public Dictionary<string, string?> Attributes
    {
        get => _attributes;
        set => _attributes = value is null
            ? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string?>(value, StringComparer.OrdinalIgnoreCase);
    }

    private Dictionary<string, string?> _attributes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Updates the identifier value and observed timestamp.
    /// </summary>
    public void Update(string value, DateTimeOffset? observedAt = null, IReadOnlyDictionary<string, string?>? attributes = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Identifier value must be provided.", nameof(value));
        }

        Value = value;
        ObservedAt = observedAt ?? DateTimeOffset.UtcNow;

        if (attributes is null)
        {
            return;
        }

        foreach (var pair in attributes)
        {
            _attributes[pair.Key] = pair.Value;
        }
    }

    /// <summary>
    /// Produces a deep copy of the current external identifier.
    /// </summary>
    public CanonExternalId Clone()
    {
        return new CanonExternalId
        {
            Scheme = Scheme,
            Value = Value,
            Source = Source,
            ObservedAt = ObservedAt,
            Attributes = new Dictionary<string, string?>(_attributes, StringComparer.OrdinalIgnoreCase)
        };
    }
}
