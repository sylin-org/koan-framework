using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;

namespace Koan.Canon.Domain.Model;

/// <summary>
/// Shared lookup index across all canonical entities.
/// </summary>
public sealed class CanonIndex : Entity<CanonIndex>
{
    /// <summary>
    /// Initializes a new index entry for the provided entity type and key.
    /// </summary>
    public CanonIndex()
    {
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// CLR type name of the canonical entity.
    /// </summary>
    [Index]
    public string EntityType { get; set; } = default!;

    /// <summary>
    /// The key value being indexed (aggregation key, external Id, etc.).
    /// </summary>
    [Index]
    public string Key { get; set; } = default!;

    /// <summary>
    /// Classifies the key to support targeted lookups.
    /// </summary>
    [Index]
    public CanonIndexKeyKind Kind { get; set; } = CanonIndexKeyKind.Aggregation;

    /// <summary>
    /// Canonical identifier that currently owns the key.
    /// </summary>
    [Index]
    public string CanonicalId { get; private set; } = default!;

    /// <summary>
    /// If the key originated from a specific source system.
    /// </summary>
    [Index]
    public string? Origin { get; private set; }

    /// <summary>
    /// Optional payload for pipeline contributors.
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
    /// Last time the index entry was touched.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Applies the canonical ownership change to the index entry.
    /// </summary>
    /// <param name="canonicalId">The canonical identifier to associate.</param>
    /// <param name="origin">Optional origin for diagnostics.</param>
    /// <param name="attributes">Optional attributes to merge.</param>
    public void Update(string canonicalId, string? origin = null, IReadOnlyDictionary<string, string?>? attributes = null)
    {
        if (string.IsNullOrWhiteSpace(canonicalId))
        {
            throw new ArgumentException("Canonical identifier must be provided.", nameof(canonicalId));
        }

        CanonicalId = canonicalId;
        Origin = origin;
        UpdatedAt = DateTimeOffset.UtcNow;

        if (attributes is null)
        {
            return;
        }

        if (_attributes.Count == 0 && attributes is Dictionary<string, string?> source && source.Comparer == StringComparer.OrdinalIgnoreCase)
        {
            _attributes = new Dictionary<string, string?>(source, StringComparer.OrdinalIgnoreCase);
            return;
        }

        foreach (var pair in attributes)
        {
            _attributes[pair.Key] = pair.Value;
        }
    }
}
