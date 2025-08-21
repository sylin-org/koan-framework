namespace Sora.Data.Abstractions.Annotations;

/// <summary>
/// Declares a logical index.
/// Recommended usage is property-anchored: place [Index] on each property that participates in the index.
/// For composite indexes, give the same Name (or Group) to each participating property and set Order to control column order.
/// If used at class-level, specify property names via Fields; adapters must resolve to storage names (respecting StorageName, naming strategies, etc.).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property, AllowMultiple = true, Inherited = true)]
public sealed class IndexAttribute : Attribute
{
    /// <summary>
    /// Optional explicit index name. When applied to multiple properties with the same Name/Group, they compose a composite index.
    /// If omitted, adapters should derive a stable name like ix_{storage}_{joined_property_names}.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Optional grouping key for composite indexes. If set, attributes sharing the same Group (or Name) compose one index.
    /// Adapters should prefer Name when both are set.
    /// </summary>
    public string? Group { get; init; }

    /// <summary>
    /// For composite indexes declared at the property level, controls the position of the property in the index.
    /// Defaults to 0; adapters should order ascending.
    /// </summary>
    public int Order { get; init; }

    /// <summary>
    /// For class-level usage only: the set of property names participating in the index. Not used when the attribute is applied to a property.
    /// </summary>
    public string[]? Fields { get; init; }

    /// <summary>
    /// Whether the index enforces uniqueness when supported.
    /// </summary>
    public bool Unique { get; init; }

    /// <summary>
    /// Helper for adapters: when attached to a property, adapters can treat it as a single-field index on that property.
    /// When class-level, returns provided Fields[] or throws if not provided.
    /// </summary>
    public static IReadOnlyList<string> ResolveFieldsFor(Type entityType, IndexAttribute index, string? anchoredPropertyName)
    {
        if (!string.IsNullOrWhiteSpace(anchoredPropertyName)) return new[] { anchoredPropertyName };
        if (index.Fields is { Length: > 0 }) return index.Fields;
        throw new InvalidOperationException($"Index on {entityType.Name} requires either anchored property or Fields[]");
    }
}
