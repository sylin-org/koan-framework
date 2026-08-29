using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Vector;

namespace Koan.Data.Connector.OpenSearch;

internal static class OpenSearchFilter
{
    internal static readonly FilterSupport Capabilities = FilterSupport.Uniform(
        nestedPaths: true,
        ignoreCase: false,
        FilterOperator.Eq, FilterOperator.Ne,
        FilterOperator.Gt, FilterOperator.Gte, FilterOperator.Lt, FilterOperator.Lte,
        FilterOperator.In, FilterOperator.Nin,
        FilterOperator.Has, FilterOperator.HasAny, FilterOperator.HasAll, FilterOperator.HasNone,
        FilterOperator.HasContains,
        FilterOperator.Size, FilterOperator.Exists);

    internal static void Write(Utf8JsonWriter writer, Filter? filter, string? scopeIdentity = null)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("bool");
        writer.WriteStartObject();
        writer.WritePropertyName("filter");
        writer.WriteStartArray();
        if (!string.IsNullOrEmpty(scopeIdentity))
            Term(writer, Infrastructure.Constants.Wire.Scope, scopeIdentity);
        if (filter is not null) Condition(writer, filter);
        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    internal static void WriteProjection(Utf8JsonWriter writer, DataObject metadata)
    {
        var projection = new Projection();
        foreach (var property in metadata.Properties)
            Project(projection, [property.Name], property.Value);

        writer.WriteStartObject();
        Entries(writer, Infrastructure.Constants.Wire.Values, projection.Values);
        writer.WritePropertyName(Infrastructure.Constants.Wire.Exists);
        writer.WriteStartArray();
        foreach (var path in projection.Exists.Order(StringComparer.Ordinal)) writer.WriteStringValue(path);
        writer.WriteEndArray();
        Entries(writer, Infrastructure.Constants.Wire.Sizes, projection.Sizes);
        writer.WriteEndObject();
    }

    private static void Project(Projection projection, IReadOnlyList<string> path, object? value)
    {
        Path(path);
        var key = PathKey(path);
        projection.Exists.Add(key);
        switch (value)
        {
            case null:
                return;
            case DataObject data:
                foreach (var property in data.Properties)
                    Project(projection, [.. path, property.Name], property.Value);
                return;
            case DataArray array:
                Add(projection.Sizes, key, Size(array.Items.Count));
                foreach (var item in array.Items)
                {
                    if (item is DataObject child)
                    {
                        foreach (var property in child.Properties)
                            Project(projection, [.. path, property.Name], property.Value);
                    }
                    else if (item is DataArray)
                        throw Unsupported(FilterOperator.Size, string.Join('.', path), "nested array projection");
                    else if (item is not null)
                        Add(projection.Values, key, Canonical(item));
                }
                return;
            default:
                Add(projection.Values, key, Canonical(value));
                return;
        }
    }

    private static void Entries(
        Utf8JsonWriter writer,
        string name,
        IReadOnlyDictionary<string, List<string>> entries)
    {
        writer.WritePropertyName(name);
        writer.WriteStartArray();
        foreach (var entry in entries.OrderBy(static item => item.Key, StringComparer.Ordinal))
            foreach (var value in entry.Value)
            {
                writer.WriteStartObject();
                writer.WriteString(Infrastructure.Constants.Wire.Path, entry.Key);
                writer.WriteString(Infrastructure.Constants.Wire.Value, value);
                writer.WriteEndObject();
            }
        writer.WriteEndArray();
    }

    private static void Add(Dictionary<string, List<string>> map, string key, string value)
    {
        if (!map.TryGetValue(key, out var values)) map[key] = values = [];
        values.Add(value);
    }

    private static void Condition(Utf8JsonWriter writer, Filter filter)
    {
        switch (filter)
        {
            case AllOf all:
                Group(writer, "filter", all.Operands);
                break;
            case AnyOf any:
                Group(writer, "should", any.Operands, minimumShouldMatch: true);
                break;
            case Not not:
                Negate(writer, nested => Condition(nested, not.Operand));
                break;
            case FieldFilter field:
                Leaf(writer, field);
                break;
            default:
                throw Unsupported(null, null, $"filter node '{filter.GetType().Name}'");
        }
    }

    private static void Group(
        Utf8JsonWriter writer,
        string clause,
        IReadOnlyList<Filter> operands,
        bool minimumShouldMatch = false)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("bool");
        writer.WriteStartObject();
        writer.WritePropertyName(clause);
        writer.WriteStartArray();
        foreach (var operand in operands) Condition(writer, operand);
        writer.WriteEndArray();
        if (minimumShouldMatch) writer.WriteNumber("minimum_should_match", 1);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void Leaf(Utf8JsonWriter writer, FieldFilter filter)
    {
        if (filter.IgnoreCase)
            throw Unsupported(filter.Operator, filter.Field.ToString(), "case-insensitive comparison");
        Path(filter.Field.Segments);
        var key = PathKey(filter.Field.Segments);
        var values = $"{Infrastructure.Constants.Wire.Index}.{Infrastructure.Constants.Wire.Values}";
        var exists = $"{Infrastructure.Constants.Wire.Index}.{Infrastructure.Constants.Wire.Exists}";
        var sizes = $"{Infrastructure.Constants.Wire.Index}.{Infrastructure.Constants.Wire.Sizes}";
        switch (filter.Operator)
        {
            case FilterOperator.Eq:
            case FilterOperator.Has:
                Equal(writer, values, exists, key, Scalar(filter));
                break;
            case FilterOperator.Ne:
                Negate(writer, nested => Equal(nested, values, exists, key, Scalar(filter)));
                break;
            case FilterOperator.Gt:
                Range(writer, values, key, "gt", Scalar(filter));
                break;
            case FilterOperator.Gte:
                Range(writer, values, key, "gte", Scalar(filter));
                break;
            case FilterOperator.Lt:
                Range(writer, values, key, "lt", Scalar(filter));
                break;
            case FilterOperator.Lte:
                Range(writer, values, key, "lte", Scalar(filter));
                break;
            case FilterOperator.In:
            case FilterOperator.HasAny:
                Terms(writer, values, key, Set(filter));
                break;
            case FilterOperator.Nin:
            case FilterOperator.HasNone:
                Negate(writer, nested => Terms(nested, values, key, Set(filter)));
                break;
            case FilterOperator.HasAll:
                AllTerms(writer, values, key, Set(filter));
                break;
            case FilterOperator.HasContains:
                Wildcard(writer, values, key, Scalar(filter));
                break;
            case FilterOperator.Size:
                var count = Convert.ToInt32(Scalar(filter), CultureInfo.InvariantCulture);
                if (count < 0) throw Unsupported(filter.Operator, filter.Field.ToString(), "negative size");
                NestedTerm(writer, sizes, key, Size(count));
                break;
            case FilterOperator.Exists:
                var present = Scalar(filter) is not bool boolean || boolean;
                if (present) Term(writer, exists, key);
                else Negate(writer, nested => Term(nested, exists, key));
                break;
            default:
                throw Unsupported(filter.Operator, filter.Field.ToString(), "operator");
        }
    }

    private static void Equal(
        Utf8JsonWriter writer,
        string values,
        string exists,
        string path,
        object? value)
    {
        if (value is null)
        {
            Negate(writer, nested => Term(nested, exists, path));
            return;
        }
        NestedTerm(writer, values, path, Canonical(value));
    }

    private static void Term(Utf8JsonWriter writer, string field, string value)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("term");
        writer.WriteStartObject();
        writer.WriteString(field, value);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void Terms(
        Utf8JsonWriter writer,
        string field,
        string path,
        IReadOnlyList<object?> values) => Nested(writer, field, path, nested =>
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

    private static void AllTerms(
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
            if (value is not null) NestedTerm(writer, field, path, Canonical(value));
        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    /// <summary>
    /// Substring against one projected value: a case-sensitive wildcard over the canonical token with
    /// the literal's wildcard metacharacters escaped, so the value matches literally. A null value is
    /// a defect in the caller's filter, not an empty match, and is refused like the range path.
    /// </summary>
    private static void Wildcard(Utf8JsonWriter writer, string field, string path, object? value)
    {
        if (value is null) throw Unsupported(FilterOperator.HasContains, path, "null substring value");
        var literal = Canonical(value);
        var pattern = "*" + literal.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("*", "\\*", StringComparison.Ordinal)
            .Replace("?", "\\?", StringComparison.Ordinal) + "*";
        Nested(writer, field, path, nested =>
        {
            nested.WriteStartObject();
            nested.WritePropertyName("wildcard");
            nested.WriteStartObject();
            nested.WritePropertyName($"{field}.{Infrastructure.Constants.Wire.Value}");
            nested.WriteStartObject();
            nested.WriteString("value", pattern);
            nested.WriteEndObject();
            nested.WriteEndObject();
            nested.WriteEndObject();
        });
    }

    private static void Range(
        Utf8JsonWriter writer,
        string field,
        string path,
        string operation,
        object? value)
    {
        if (value is null) throw Unsupported(null, field, "null range value");
        Nested(writer, field, path, nested =>
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

    private static void NestedTerm(Utf8JsonWriter writer, string field, string path, string value) =>
        Nested(writer, field, path,
            nested => Term(nested, $"{field}.{Infrastructure.Constants.Wire.Value}", value));

    private static void Nested(
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
        Term(writer, $"{field}.{Infrastructure.Constants.Wire.Path}", path);
        condition(writer);
        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void Negate(Utf8JsonWriter writer, Action<Utf8JsonWriter> condition)
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
        byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal =>
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

    private static string PathKey(IReadOnlyList<string> segments) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(string.Join('\u001f', segments)))).ToLowerInvariant();

    private static void Path(IReadOnlyList<string> segments)
    {
        if (segments.Count == 0 || segments.Any(static segment => string.IsNullOrWhiteSpace(segment)))
            throw Unsupported(null, string.Join('.', segments), "empty path segment");
    }

    private static VectorFilterUnsupportedException Unsupported(
        FilterOperator? operation,
        string? field,
        string detail) => new(
            Infrastructure.Constants.Provider.Name,
            operation,
            field,
            $"OpenSearch cannot faithfully push {detail}. Narrow the filter or select an adapter that declares it.");

    private sealed class Projection
    {
        internal Dictionary<string, List<string>> Values { get; } = new(StringComparer.Ordinal);
        internal HashSet<string> Exists { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, List<string>> Sizes { get; } = new(StringComparer.Ordinal);
    }
}
