using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Vector;

namespace Koan.Data.Connector.ElasticSearch;

internal static class ElasticSearchFilter
{
    internal static readonly FilterSupport Capabilities = FilterSupport.Uniform(
        nestedPaths: true,
        ignoreCase: false,
        FilterOperator.Eq, FilterOperator.Ne,
        FilterOperator.Gt, FilterOperator.Gte, FilterOperator.Lt, FilterOperator.Lte,
        FilterOperator.In, FilterOperator.Nin,
        FilterOperator.Has, FilterOperator.HasAny, FilterOperator.HasAll, FilterOperator.HasNone,
        FilterOperator.Size, FilterOperator.Exists);

    internal static void Write(Utf8JsonWriter writer, Filter? filter, string? scopeIdentity = null)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("bool");
        writer.WriteStartObject();
        writer.WritePropertyName("filter");
        writer.WriteStartArray();
        if (!string.IsNullOrEmpty(scopeIdentity))
            WriteTerm(writer, Infrastructure.Constants.Wire.Scope, scopeIdentity);
        if (filter is not null) WriteCondition(writer, filter);
        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    internal static void WriteProjection(Utf8JsonWriter writer, DataObject metadata)
    {
        var projection = new Projection();
        foreach (var property in metadata.Properties)
            Add(projection, [property.Name], property.Value);

        writer.WriteStartObject();
        WriteEntries(writer, Infrastructure.Constants.Wire.Values, projection.Values);
        writer.WritePropertyName(Infrastructure.Constants.Wire.Exists);
        writer.WriteStartArray();
        foreach (var path in projection.Exists.Order(StringComparer.Ordinal)) writer.WriteStringValue(path);
        writer.WriteEndArray();
        WriteEntries(writer, Infrastructure.Constants.Wire.Sizes, projection.Sizes);
        writer.WriteEndObject();
    }

    private static void Add(Projection projection, IReadOnlyList<string> path, object? value)
    {
        ValidatePath(path);
        var key = Key(path);
        projection.Exists.Add(key);
        switch (value)
        {
            case null:
                return;
            case DataObject data:
                foreach (var property in data.Properties)
                    Add(projection, [.. path, property.Name], property.Value);
                return;
            case DataArray array:
                AddValue(projection.Sizes, key, Size(array.Items.Count));
                foreach (var item in array.Items)
                {
                    if (item is DataObject nested)
                    {
                        foreach (var property in nested.Properties)
                            Add(projection, [.. path, property.Name], property.Value);
                    }
                    else if (item is DataArray)
                    {
                        throw Unsupported(FilterOperator.Size, string.Join('.', path), "nested array projection");
                    }
                    else if (item is not null)
                    {
                        AddValue(projection.Values, key, Canonical(item));
                    }
                }
                return;
            default:
                AddValue(projection.Values, key, Canonical(value));
                return;
        }
    }

    private static void WriteEntries(
        Utf8JsonWriter writer,
        string name,
        IReadOnlyDictionary<string, List<string>> values)
    {
        writer.WritePropertyName(name);
        writer.WriteStartArray();
        foreach (var entry in values.OrderBy(static item => item.Key, StringComparer.Ordinal))
        {
            foreach (var value in entry.Value)
            {
                writer.WriteStartObject();
                writer.WriteString(Infrastructure.Constants.Wire.Path, entry.Key);
                writer.WriteString(Infrastructure.Constants.Wire.Value, value);
                writer.WriteEndObject();
            }
        }
        writer.WriteEndArray();
    }

    private static void AddValue(Dictionary<string, List<string>> map, string key, string value)
    {
        if (!map.TryGetValue(key, out var values)) map[key] = values = [];
        values.Add(value);
    }

    private static void WriteCondition(Utf8JsonWriter writer, Filter filter)
    {
        switch (filter)
        {
            case AllOf all:
                WriteGroup(writer, "filter", all.Operands);
                return;
            case AnyOf any:
                WriteAny(writer, any.Operands);
                return;
            case Not not:
                WriteNegated(writer, nested => WriteCondition(nested, not.Operand));
                return;
            case FieldFilter field:
                WriteLeaf(writer, field);
                return;
            default:
                throw Unsupported(null, null, $"filter node '{filter.GetType().Name}'");
        }
    }

    private static void WriteGroup(Utf8JsonWriter writer, string clause, IReadOnlyList<Filter> operands)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("bool");
        writer.WriteStartObject();
        writer.WritePropertyName(clause);
        writer.WriteStartArray();
        foreach (var operand in operands) WriteCondition(writer, operand);
        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteAny(Utf8JsonWriter writer, IReadOnlyList<Filter> operands)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("bool");
        writer.WriteStartObject();
        writer.WritePropertyName("should");
        writer.WriteStartArray();
        foreach (var operand in operands) WriteCondition(writer, operand);
        writer.WriteEndArray();
        writer.WriteNumber("minimum_should_match", 1);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteLeaf(Utf8JsonWriter writer, FieldFilter field)
    {
        if (field.IgnoreCase)
            throw Unsupported(field.Operator, field.Field.ToString(), "case-insensitive comparison");
        ValidatePath(field.Field.Segments);
        var hash = Key(field.Field.Segments);
        var valueField = $"{Infrastructure.Constants.Wire.Index}.{Infrastructure.Constants.Wire.Values}";
        var existsField = $"{Infrastructure.Constants.Wire.Index}.{Infrastructure.Constants.Wire.Exists}";
        var sizeField = $"{Infrastructure.Constants.Wire.Index}.{Infrastructure.Constants.Wire.Sizes}";
        switch (field.Operator)
        {
            case FilterOperator.Eq:
            case FilterOperator.Has:
                WriteEqual(writer, valueField, existsField, hash, Scalar(field));
                return;
            case FilterOperator.Ne:
                WriteNegated(writer, nested => WriteEqual(nested, valueField, existsField, hash, Scalar(field)));
                return;
            case FilterOperator.Gt:
                WriteRange(writer, valueField, hash, "gt", Scalar(field));
                return;
            case FilterOperator.Gte:
                WriteRange(writer, valueField, hash, "gte", Scalar(field));
                return;
            case FilterOperator.Lt:
                WriteRange(writer, valueField, hash, "lt", Scalar(field));
                return;
            case FilterOperator.Lte:
                WriteRange(writer, valueField, hash, "lte", Scalar(field));
                return;
            case FilterOperator.In:
            case FilterOperator.HasAny:
                WriteTerms(writer, valueField, hash, Set(field));
                return;
            case FilterOperator.Nin:
            case FilterOperator.HasNone:
                WriteNegated(writer, nested => WriteTerms(nested, valueField, hash, Set(field)));
                return;
            case FilterOperator.HasAll:
                WriteAllTerms(writer, valueField, hash, Set(field));
                return;
            case FilterOperator.Size:
                var count = Convert.ToInt32(Scalar(field), CultureInfo.InvariantCulture);
                if (count < 0) throw Unsupported(field.Operator, field.Field.ToString(), "negative size");
                WriteNestedTerm(writer, sizeField, hash, Size(count));
                return;
            case FilterOperator.Exists:
                var present = Scalar(field) is not bool boolean || boolean;
                if (present) WriteTerm(writer, existsField, hash);
                else WriteNegated(writer, nested => WriteTerm(nested, existsField, hash));
                return;
            default:
                throw Unsupported(field.Operator, field.Field.ToString(), "operator");
        }
    }

    private static void WriteEqual(
        Utf8JsonWriter writer,
        string field,
        string existsField,
        string path,
        object? value)
    {
        if (value is null)
        {
            WriteNegated(writer, nested => WriteTerm(nested, existsField, path));
            return;
        }
        WriteNestedTerm(writer, field, path, Canonical(value));
    }

    private static void WriteTerm(Utf8JsonWriter writer, string field, string value)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("term");
        writer.WriteStartObject();
        writer.WriteString(field, value);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteTerms(
        Utf8JsonWriter writer,
        string field,
        string path,
        IReadOnlyList<object?> values)
    {
        WriteNested(writer, field, path, nested =>
        {
            nested.WriteStartObject();
            nested.WritePropertyName("terms");
            nested.WriteStartObject();
            nested.WritePropertyName($"{field}.{Infrastructure.Constants.Wire.Value}");
            nested.WriteStartArray();
            foreach (var value in values)
                if (value is not null) nested.WriteStringValue(Canonical(value));
            nested.WriteEndArray();
            nested.WriteEndObject();
            nested.WriteEndObject();
        });
    }

    private static void WriteAllTerms(
        Utf8JsonWriter writer,
        string field,
        string path,
        IReadOnlyList<object?> values)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("bool");
        writer.WriteStartObject();
        writer.WritePropertyName("filter");
        writer.WriteStartArray();
        foreach (var value in values)
            if (value is not null) WriteNestedTerm(writer, field, path, Canonical(value));
        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteRange(
        Utf8JsonWriter writer,
        string field,
        string path,
        string operation,
        object? value)
    {
        if (value is null) throw Unsupported(null, field, "null range value");
        WriteNested(writer, field, path, nested =>
        {
            nested.WriteStartObject();
            nested.WritePropertyName("range");
            nested.WriteStartObject();
            nested.WritePropertyName($"{field}.{Infrastructure.Constants.Wire.Value}");
            nested.WriteStartObject();
            nested.WriteString(operation, Canonical(value));
            nested.WriteEndObject();
            nested.WriteEndObject();
            nested.WriteEndObject();
        });
    }

    private static void WriteNestedTerm(Utf8JsonWriter writer, string field, string path, string value) =>
        WriteNested(writer, field, path,
            nested => WriteTerm(nested, $"{field}.{Infrastructure.Constants.Wire.Value}", value));

    private static void WriteNested(
        Utf8JsonWriter writer,
        string field,
        string path,
        Action<Utf8JsonWriter> condition)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("nested");
        writer.WriteStartObject();
        writer.WriteString("path", field);
        writer.WritePropertyName("query");
        writer.WriteStartObject();
        writer.WritePropertyName("bool");
        writer.WriteStartObject();
        writer.WritePropertyName("filter");
        writer.WriteStartArray();
        WriteTerm(writer, $"{field}.{Infrastructure.Constants.Wire.Path}", path);
        condition(writer);
        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteNegated(Utf8JsonWriter writer, Action<Utf8JsonWriter> condition)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("bool");
        writer.WriteStartObject();
        writer.WritePropertyName("must_not");
        writer.WriteStartArray();
        condition(writer);
        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static object? Scalar(FieldFilter filter) => filter.Value switch
    {
        FilterValue.Scalar scalar => scalar.Value,
        FilterValue.Set set when set.Values.Count > 0 => set.Values[0],
        FilterValue.None => null,
        _ => null
    };

    private static IReadOnlyList<object?> Set(FieldFilter filter) => filter.Value switch
    {
        FilterValue.Set set => set.Values,
        FilterValue.Scalar scalar => [scalar.Value],
        _ => []
    };

    private static string Canonical(object value) => value switch
    {
        string text => "s:" + text,
        bool boolean => boolean ? "b:1" : "b:0",
        sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal =>
            "n:" + Sortable(Convert.ToDouble(value, CultureInfo.InvariantCulture)),
        Guid guid => "g:" + guid.ToString("D"),
        DateOnly date => "d:" + date.ToString("O", CultureInfo.InvariantCulture),
        TimeOnly time => "t:" + time.ToString("O", CultureInfo.InvariantCulture),
        DateTime date => "dt:" + date.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset date => "dto:" + date.ToString("O", CultureInfo.InvariantCulture),
        TimeSpan duration => "du:" + duration.ToString("c", CultureInfo.InvariantCulture),
        byte[] bytes => "x:" + Convert.ToBase64String(bytes),
        Enum enumeration => "n:" + Sortable(Convert.ToDouble(enumeration, CultureInfo.InvariantCulture)),
        _ => throw Unsupported(null, null, $"value type '{value.GetType().FullName}'")
    };

    private static string Sortable(double value)
    {
        if (!double.IsFinite(value)) throw Unsupported(null, null, "non-finite numeric value");
        var bits = unchecked((ulong)BitConverter.DoubleToInt64Bits(value));
        bits = (bits & 0x8000_0000_0000_0000UL) != 0 ? ~bits : bits ^ 0x8000_0000_0000_0000UL;
        return bits.ToString("x16", CultureInfo.InvariantCulture);
    }

    private static string Size(int value) => value.ToString("x8", CultureInfo.InvariantCulture);

    private static string Key(IReadOnlyList<string> segments)
    {
        ValidatePath(segments);
        return Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(string.Join('\u001f', segments)))).ToLowerInvariant();
    }

    private static void ValidatePath(IReadOnlyList<string> segments)
    {
        if (segments.Count == 0 || segments.Any(static segment => string.IsNullOrWhiteSpace(segment)))
            throw Unsupported(null, string.Join('.', segments), "empty path segment");
    }

    private static VectorFilterUnsupportedException Unsupported(
        FilterOperator? operation,
        string? field,
        string detail) =>
        new(Infrastructure.Constants.Provider.Name, operation, field,
            $"Elasticsearch cannot faithfully push {detail}. Narrow the filter or select an adapter that declares it.");

    private sealed class Projection
    {
        internal Dictionary<string, List<string>> Values { get; } = new(StringComparer.Ordinal);
        internal HashSet<string> Exists { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, List<string>> Sizes { get; } = new(StringComparer.Ordinal);
    }
}
