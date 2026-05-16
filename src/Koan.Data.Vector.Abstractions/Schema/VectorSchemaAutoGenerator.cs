using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Koan.Data.Vector.Abstractions.Schema;

internal static class VectorSchemaAutoGenerator
{
    private static readonly ConcurrentDictionary<Type, VectorSchemaDescriptor?> Cache = new();

    public static VectorSchemaDescriptor? TryCreate(Type entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType);

        return Cache.GetOrAdd(entityType, static type => Build(type));
    }

    private static VectorSchemaDescriptor? Build(Type entityType)
    {
        var schemaAttribute = entityType.GetCustomAttribute<VectorSchemaAttribute>();
        if (schemaAttribute is null)
        {
            return null;
        }

        var metadataType = schemaAttribute.MetadataType;
        if (metadataType is null)
        {
            return null;
        }

        var propertyDescriptors = DescribeMetadata(metadataType);
        var schemaProperties = propertyDescriptors
            .Select(static p => new VectorSchemaProperty(
                p.Name,
                p.Type,
                p.IsRequired,
                p.IsSearchable,
                p.IsFilterable,
                p.IsSortable,
                p.Description))
            .ToArray();

        var projector = CreateProjector(metadataType, propertyDescriptors);
        var entityName = string.IsNullOrWhiteSpace(schemaAttribute.EntityName)
            ? entityType.Name
            : schemaAttribute.EntityName!;

        return new VectorSchemaDescriptor(
            entityType,
            entityName,
            schemaProperties,
            projector,
            metadataType,
            isFallback: false);
    }

    private static Func<object?, IReadOnlyDictionary<string, object?>?> CreateProjector(
        Type metadataType,
        IReadOnlyList<MetadataProperty> descriptors)
    {
        return metadata =>
        {
            if (metadata is null)
            {
                return null;
            }

            if (metadata is IReadOnlyDictionary<string, object?> readOnly)
            {
                return readOnly;
            }

            if (metadata is IDictionary<string, object?> dict)
            {
                return Normalize(dict);
            }

            if (metadata is IEnumerable<KeyValuePair<string, object?>> kvps)
            {
                return Normalize(kvps);
            }

            if (!metadataType.IsInstanceOfType(metadata))
            {
                return null;
            }

            if (metadata is IVectorMetadataDictionary vectorMetadata)
            {
                return Normalize(vectorMetadata.ToDictionary());
            }

            var map = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var descriptor in descriptors)
            {
                var value = descriptor.Property.GetValue(metadata);
                map[descriptor.Name] = value;
            }

            return map;
        };
    }

    private static IReadOnlyList<MetadataProperty> DescribeMetadata(Type metadataType)
    {
        var properties = metadataType.GetProperties(BindingFlags.Instance | BindingFlags.Public);
        var result = new List<MetadataProperty>(properties.Length);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var property in properties)
        {
            if (property.GetIndexParameters().Length > 0 || !property.CanRead)
            {
                continue;
            }

            var attribute = property.GetCustomAttribute<VectorPropertyAttribute>();
            if (attribute?.Ignore == true)
            {
                continue;
            }

            var schemaName = attribute?.Name ?? ToCamelCase(property.Name);
            if (!seen.Add(schemaName))
            {
                continue;
            }

            var propertyType = attribute?.Type ?? Infer(property.PropertyType);
            if (propertyType is null)
            {
                continue;
            }

            var descriptor = new MetadataProperty(
                property,
                schemaName,
                propertyType.Value,
                attribute?.Required ?? IsStrictlyRequired(property.PropertyType),
                attribute?.Searchable ?? false,
                attribute?.Filterable ?? false,
                attribute?.Sortable ?? false,
                attribute?.Description);

            result.Add(descriptor);
        }

        return result;
    }

    private static VectorSchemaPropertyType? Infer(Type clrType)
    {
        var type = Nullable.GetUnderlyingType(clrType) ?? clrType;

        if (type == typeof(string))
        {
            return VectorSchemaPropertyType.Text;
        }

        if (type.IsArray && type.GetElementType() == typeof(string))
        {
            return VectorSchemaPropertyType.TextArray;
        }

        if (typeof(IEnumerable<string>).IsAssignableFrom(type) && type != typeof(string))
        {
            return VectorSchemaPropertyType.TextArray;
        }

        if (type == typeof(int))
        {
            return VectorSchemaPropertyType.Int;
        }

        if (type == typeof(long))
        {
            return VectorSchemaPropertyType.Long;
        }

        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
        {
            return VectorSchemaPropertyType.Number;
        }

        if (type == typeof(bool))
        {
            return VectorSchemaPropertyType.Boolean;
        }

        if (type == typeof(DateTime) || type == typeof(DateTimeOffset))
        {
            return VectorSchemaPropertyType.DateTime;
        }

        return null;
    }

    private static bool IsStrictlyRequired(Type clrType)
    {
        if (!clrType.IsValueType)
        {
            return false;
        }

        return Nullable.GetUnderlyingType(clrType) is null;
    }

    private static string ToCamelCase(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        if (value.Length == 1)
        {
            return value.ToLowerInvariant();
        }

        return char.ToLowerInvariant(value[0]) + value.Substring(1);
    }

    private static IReadOnlyDictionary<string, object?> Normalize(IReadOnlyDictionary<string, object?> source)
    {
        if (source is Dictionary<string, object?> dictionary && dictionary.Comparer.Equals(StringComparer.OrdinalIgnoreCase))
        {
            return dictionary;
        }

        var map = new Dictionary<string, object?>(source.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in source)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                continue;
            }

            map[pair.Key] = pair.Value;
        }

        return map;
    }

    private static IReadOnlyDictionary<string, object?> Normalize(IDictionary<string, object?> source)
        => source is IReadOnlyDictionary<string, object?> readOnly
            ? Normalize(readOnly)
            : Normalize(source.AsEnumerable());

    private static IReadOnlyDictionary<string, object?> Normalize(IEnumerable<KeyValuePair<string, object?>> pairs)
    {
        var dictionary = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in pairs)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                continue;
            }

            dictionary[pair.Key] = pair.Value;
        }

        return dictionary;
    }

    private sealed record MetadataProperty(
        PropertyInfo Property,
        string Name,
        VectorSchemaPropertyType Type,
        bool IsRequired,
        bool IsSearchable,
        bool IsFilterable,
        bool IsSortable,
        string? Description);
}
