using System.Globalization;
using System.Text.Json;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;

namespace Koan.Data.Vector.Connector.Qdrant;

internal static class QdrantFilter
{
    internal static readonly FilterSupport Capabilities = FilterSupport.Uniform(
        nestedPaths: true,
        ignoreCase: false,
        FilterOperator.Eq, FilterOperator.Ne,
        FilterOperator.Gt, FilterOperator.Gte, FilterOperator.Lt, FilterOperator.Lte,
        FilterOperator.In, FilterOperator.Nin,
        FilterOperator.Has, FilterOperator.HasAny, FilterOperator.HasAll, FilterOperator.HasNone,
        FilterOperator.Size, FilterOperator.Exists);

    internal static void Write(
        Utf8JsonWriter writer,
        Filter? filter,
        Action<Utf8JsonWriter>? additionalMust = null)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("must");
        writer.WriteStartArray();
        if (filter is not null) WriteCondition(writer, filter);
        additionalMust?.Invoke(writer);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    internal static void WriteIndex(Utf8JsonWriter writer, DataObject metadata)
    {
        writer.WriteStartObject();
        foreach (var property in metadata.Properties)
        {
            writer.WritePropertyName(property.Name);
            WriteValue(writer, property.Value);
        }
        writer.WriteEndObject();
    }

    private static void WriteCondition(Utf8JsonWriter writer, Filter filter)
    {
        switch (filter)
        {
            case AllOf all:
                WriteGroup(writer, "must", all.Operands);
                return;
            case AnyOf any:
                WriteGroup(writer, "should", any.Operands);
                return;
            case Not not:
                WriteGroup(writer, "must_not", [not.Operand]);
                return;
            case FieldFilter field:
                WriteLeaf(writer, field);
                return;
            default:
                throw Unsupported(null, null, $"filter node '{filter.GetType().Name}'");
        }
    }

    private static void WriteGroup(Utf8JsonWriter writer, string name, IReadOnlyList<Filter> operands)
    {
        writer.WriteStartObject();
        writer.WritePropertyName(name);
        writer.WriteStartArray();
        foreach (var operand in operands) WriteCondition(writer, operand);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteLeaf(Utf8JsonWriter writer, FieldFilter field)
    {
        if (field.IgnoreCase)
            throw Unsupported(field.Operator, field.Field.ToString(), "case-insensitive comparison");
        var key = Key(field.Field.Segments);
        switch (field.Operator)
        {
            case FilterOperator.Eq:
            case FilterOperator.Has:
                WriteMatch(writer, key, Scalar(field));
                return;
            case FilterOperator.Ne:
                WriteNegated(writer, w => WriteMatch(w, key, Scalar(field)));
                return;
            case FilterOperator.Gt:
                WriteRange(writer, key, "gt", Scalar(field));
                return;
            case FilterOperator.Gte:
                WriteRange(writer, key, "gte", Scalar(field));
                return;
            case FilterOperator.Lt:
                WriteRange(writer, key, "lt", Scalar(field));
                return;
            case FilterOperator.Lte:
                WriteRange(writer, key, "lte", Scalar(field));
                return;
            case FilterOperator.In:
            case FilterOperator.HasAny:
                WriteAny(writer, key, Set(field));
                return;
            case FilterOperator.Nin:
            case FilterOperator.HasNone:
                WriteNegated(writer, w => WriteAny(w, key, Set(field)));
                return;
            case FilterOperator.HasAll:
                WriteHasAll(writer, key, Set(field));
                return;
            case FilterOperator.Size:
                WriteSize(writer, key, Scalar(field));
                return;
            case FilterOperator.Exists:
                var present = Scalar(field) is not bool value || value;
                if (present) WriteNegated(writer, w => WriteIsEmpty(w, key));
                else WriteIsEmpty(writer, key);
                return;
            default:
                throw Unsupported(field.Operator, field.Field.ToString(), "operator");
        }
    }

    private static void WriteMatch(Utf8JsonWriter writer, string key, object? value)
    {
        if (value is null)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("is_null");
            writer.WriteStartObject();
            writer.WriteString("key", key);
            writer.WriteEndObject();
            writer.WriteEndObject();
            return;
        }
        writer.WriteStartObject();
        writer.WriteString("key", key);
        writer.WritePropertyName("match");
        writer.WriteStartObject();
        writer.WritePropertyName("value");
        WriteValue(writer, value);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteAny(Utf8JsonWriter writer, string key, IReadOnlyList<object?> values)
    {
        writer.WriteStartObject();
        writer.WriteString("key", key);
        writer.WritePropertyName("match");
        writer.WriteStartObject();
        writer.WritePropertyName("any");
        writer.WriteStartArray();
        foreach (var value in values) WriteValue(writer, value);
        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteRange(Utf8JsonWriter writer, string key, string operation, object? value)
    {
        if (value is null) throw Unsupported(null, key, "null range value");
        writer.WriteStartObject();
        writer.WriteString("key", key);
        writer.WritePropertyName("range");
        writer.WriteStartObject();
        writer.WritePropertyName(operation);
        WriteValue(writer, value);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteNegated(Utf8JsonWriter writer, Action<Utf8JsonWriter> condition)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("must_not");
        writer.WriteStartArray();
        condition(writer);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteHasAll(Utf8JsonWriter writer, string key, IReadOnlyList<object?> values)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("must");
        writer.WriteStartArray();
        foreach (var value in values) WriteMatch(writer, key, value);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteSize(Utf8JsonWriter writer, string key, object? value)
    {
        var count = Convert.ToInt32(value, CultureInfo.InvariantCulture);
        if (count < 0) throw Unsupported(FilterOperator.Size, key, "negative size");
        writer.WriteStartObject();
        writer.WriteString("key", key);
        writer.WritePropertyName("values_count");
        writer.WriteStartObject();
        writer.WriteNumber("gte", count);
        writer.WriteNumber("lte", count);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteIsEmpty(Utf8JsonWriter writer, string key)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("is_empty");
        writer.WriteStartObject();
        writer.WriteString("key", key);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static string Key(IReadOnlyList<string> segments)
    {
        if (segments.Count == 0 || segments.Any(static segment =>
                string.IsNullOrWhiteSpace(segment) || segment.Contains('.', StringComparison.Ordinal)))
            throw Unsupported(null, string.Join('.', segments), "empty or dot-containing path segment");
        return Infrastructure.Constants.Wire.Index + "." + string.Join('.', segments);
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

    private static void WriteValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null: writer.WriteNullValue(); break;
            case string text: writer.WriteStringValue(text); break;
            case bool boolean: writer.WriteBooleanValue(boolean); break;
            case sbyte number: writer.WriteNumberValue(number); break;
            case byte number: writer.WriteNumberValue(number); break;
            case short number: writer.WriteNumberValue(number); break;
            case ushort number: writer.WriteNumberValue(number); break;
            case int number: writer.WriteNumberValue(number); break;
            case uint number: writer.WriteNumberValue(number); break;
            case long number: writer.WriteNumberValue(number); break;
            case ulong number: writer.WriteNumberValue(number); break;
            case float number when float.IsFinite(number): writer.WriteNumberValue(number); break;
            case double number when double.IsFinite(number): writer.WriteNumberValue(number); break;
            case decimal number: writer.WriteNumberValue(number); break;
            case Guid guid: writer.WriteStringValue(guid); break;
            case DateOnly date: writer.WriteStringValue(date.ToString("O", CultureInfo.InvariantCulture)); break;
            case TimeOnly time: writer.WriteStringValue(time.ToString("O", CultureInfo.InvariantCulture)); break;
            case DateTime date: writer.WriteStringValue(date.ToString("O", CultureInfo.InvariantCulture)); break;
            case DateTimeOffset date: writer.WriteStringValue(date.ToString("O", CultureInfo.InvariantCulture)); break;
            case TimeSpan duration: writer.WriteStringValue(duration.ToString("c", CultureInfo.InvariantCulture)); break;
            case byte[] bytes: writer.WriteBase64StringValue(bytes); break;
            case DataObject data: WriteIndex(writer, data); break;
            case DataArray array:
                writer.WriteStartArray();
                foreach (var item in array.Items) WriteValue(writer, item);
                writer.WriteEndArray();
                break;
            default:
                throw Unsupported(null, null, $"value type '{value.GetType().FullName}'");
        }
    }

    private static VectorFilterUnsupportedException Unsupported(FilterOperator? operation, string? field, string detail) =>
        new(Infrastructure.Constants.Provider.Name, operation, field,
            $"Qdrant cannot faithfully push {detail}. Narrow the filter or select an adapter that declares it.");
}
