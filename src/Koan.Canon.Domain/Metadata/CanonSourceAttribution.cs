namespace Koan.Canon.Domain.Metadata;

/// <summary>
/// Captures diagnostics about a source system contributing to canonization.
/// </summary>
public sealed class CanonSourceAttribution
{
    /// <summary>
    /// Logical key representing the source (adapter, integration, etc.).
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Human friendly name for reporting.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Optional channel the source used (e.g., transport, API route).
    /// </summary>
    public string? Channel { get; set; }

    /// <summary>
    /// Last time a payload from this source contributed to canonization.
    /// </summary>
    public DateTimeOffset SeenAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Arbitrary attributes for diagnostics.
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
    /// Updates an attribute value.
    /// </summary>
    public void SetAttribute(string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Attribute key must be provided.", nameof(key));
        }

        _attributes[key] = value;
    }

    /// <summary>
    /// Clones the attribution instance.
    /// </summary>
    public CanonSourceAttribution Clone()
    {
        return new CanonSourceAttribution
        {
            Key = Key,
            DisplayName = DisplayName,
            Channel = Channel,
            SeenAt = SeenAt,
            Attributes = new Dictionary<string, string?>(_attributes, StringComparer.OrdinalIgnoreCase)
        };
    }
}
