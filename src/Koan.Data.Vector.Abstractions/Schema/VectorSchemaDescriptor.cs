using System;
using System.Collections.Generic;
using System.Linq;

namespace Koan.Data.Vector.Abstractions.Schema;

/// <summary>
/// Complete schema description for a single vector-enabled entity.
/// Provides strongly-typed metadata projection for adapter consumption.
/// </summary>
public sealed class VectorSchemaDescriptor
{
    private readonly Func<object?, IReadOnlyDictionary<string, object?>?> _metadataProjector;

    internal VectorSchemaDescriptor(
        Type entityType,
        string entityName,
        IReadOnlyList<VectorSchemaProperty> properties,
        Func<object?, IReadOnlyDictionary<string, object?>?> metadataProjector,
        Type? metadataType,
        bool isFallback = false)
    {
        EntityType = entityType ?? throw new ArgumentNullException(nameof(entityType));
        EntityName = string.IsNullOrWhiteSpace(entityName) ? entityType.Name : entityName;
        Properties = properties ?? throw new ArgumentNullException(nameof(properties));
        _metadataProjector = metadataProjector ?? throw new ArgumentNullException(nameof(metadataProjector));
        MetadataType = metadataType;
        IsFallback = isFallback;
    }

    public Type EntityType { get; }
    public string EntityName { get; }
    public IReadOnlyList<VectorSchemaProperty> Properties { get; }
    public Type? MetadataType { get; }
    public bool IsFallback { get; }

    /// <summary>
    /// Projects arbitrary metadata into a provider-ready dictionary.
    /// Returns null when no metadata is supplied.
    /// </summary>
    public IReadOnlyDictionary<string, object?>? ProjectMetadata(object? metadata)
    {
        if (metadata is null)
        {
            return null;
        }

        var projected = _metadataProjector(metadata);
        if (projected is not null)
        {
            return projected;
        }

        return Coerce(metadata);
    }

    public static VectorSchemaDescriptor CreateFallback(Type entityType)
    {
        return new VectorSchemaDescriptor(
            entityType,
            entityType.Name,
            Array.Empty<VectorSchemaProperty>(),
            Coerce,
            null,
            isFallback: true);
    }

    private static IReadOnlyDictionary<string, object?>? Coerce(object? metadata)
    {
        switch (metadata)
        {
            case IReadOnlyDictionary<string, object?> readOnly:
                return readOnly;
            case IDictionary<string, object?> dict:
                return new Dictionary<string, object?>(dict, StringComparer.OrdinalIgnoreCase);
            case IEnumerable<KeyValuePair<string, object?>> kvps:
                {
                    var materialized = kvps.ToDictionary(static kvp => kvp.Key, static kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);
                    return materialized;
                }
            default:
                return null;
        }
    }
}
