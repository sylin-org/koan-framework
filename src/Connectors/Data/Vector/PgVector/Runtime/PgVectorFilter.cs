using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Npgsql;

namespace Koan.Data.Vector.Connector.PgVector;

internal static class PgVectorFilter
{
    private const string TypeTag = "__koan_type";
    private const string TaggedValue = "value";

    internal static readonly FilterSupport Capabilities = FilterSupport.Uniform(
        nestedPaths: true,
        ignoreCase: false,
        FilterOperator.Eq,
        FilterOperator.Ne,
        FilterOperator.Gt,
        FilterOperator.Gte,
        FilterOperator.Lt,
        FilterOperator.Lte,
        FilterOperator.In,
        FilterOperator.Nin,
        FilterOperator.Has,
        FilterOperator.HasAny,
        FilterOperator.HasAll,
        FilterOperator.HasNone,
        FilterOperator.Size,
        FilterOperator.Exists);

    internal static string? ToIndexJson(DataObject? metadata)
    {
        if (metadata is null) return null;
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer)) WriteObject(writer, metadata);
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    internal static string Compile(Filter? filter, NpgsqlCommand command, string column)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (filter is null) return "TRUE";
        var compiler = new Compiler(command, column);
        return compiler.Write(filter);
    }

    private static void WriteObject(Utf8JsonWriter writer, DataObject value)
    {
        writer.WriteStartObject();
        foreach (var property in value.Properties)
        {
            writer.WritePropertyName(property.Name);
            WriteValue(writer, property.Value);
        }
        writer.WriteEndObject();
    }

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
            case Guid guid: WriteTagged(writer, "guid", guid.ToString("D")); break;
            case DateOnly date: WriteTagged(writer, "date", date.DayNumber); break;
            case TimeOnly time: WriteTagged(writer, "time", time.Ticks); break;
            case DateTime date: WriteTagged(writer, "datetime", date.Ticks); break;
            case DateTimeOffset date: WriteTagged(writer, "datetimeoffset", date.UtcDateTime.Ticks); break;
            case TimeSpan duration: WriteTagged(writer, "timespan", duration.Ticks); break;
            case byte[] bytes: WriteTagged(writer, "bytes", Convert.ToBase64String(bytes)); break;
            case DataObject data: WriteObject(writer, data); break;
            case DataArray array:
                writer.WriteStartArray();
                foreach (var item in array.Items) WriteValue(writer, item);
                writer.WriteEndArray();
                break;
            default:
                throw Unsupported(null, null, $"metadata value type '{value.GetType().FullName}'");
        }
    }

    private static void WriteTagged(Utf8JsonWriter writer, string type, string value)
    {
        writer.WriteStartObject();
        writer.WriteString(TypeTag, type);
        writer.WriteString(TaggedValue, value);
        writer.WriteEndObject();
    }

    private static void WriteTagged(Utf8JsonWriter writer, string type, long value)
    {
        writer.WriteStartObject();
        writer.WriteString(TypeTag, type);
        writer.WriteNumber(TaggedValue, value);
        writer.WriteEndObject();
    }

    private sealed class Compiler(NpgsqlCommand command, string column)
    {
        private int _parameter;

        internal string Write(Filter filter) => filter switch
        {
            AllOf all => Group(all.Operands, "AND", identity: "TRUE"),
            AnyOf any => Group(any.Operands, "OR", identity: "FALSE"),
            Not not => $"(NOT {Write(not.Operand)})",
            FieldFilter field => WriteField(field),
            _ => throw Unsupported(null, null, $"filter node '{filter.GetType().Name}'")
        };

        private string Group(IReadOnlyList<Filter> operands, string operation, string identity) =>
            operands.Count == 0
                ? identity
                : $"({string.Join($" {operation} ", operands.Select(Write))})";

        private string WriteField(FieldFilter field)
        {
            if (field.IgnoreCase)
                throw Unsupported(field.Operator, field.Field.ToString(), "case-insensitive comparison");
            if (field.Field.Segments.Count == 0 || field.Field.Segments.Any(string.IsNullOrWhiteSpace))
                throw Unsupported(field.Operator, field.Field.ToString(), "empty metadata path");
            var path = Bind(field.Field.Segments.ToArray());
            var expression = $"({column} #> @{path})";
            return field.Operator switch
            {
                FilterOperator.Eq => Equal(expression, Scalar(field)),
                FilterOperator.Ne => $"(NOT {Equal(expression, Scalar(field))})",
                FilterOperator.Gt => Compare(expression, ">", Scalar(field), field),
                FilterOperator.Gte => Compare(expression, ">=", Scalar(field), field),
                FilterOperator.Lt => Compare(expression, "<", Scalar(field), field),
                FilterOperator.Lte => Compare(expression, "<=", Scalar(field), field),
                FilterOperator.In => AnyEqual(expression, Set(field), negate: false),
                FilterOperator.Nin => AnyEqual(expression, Set(field), negate: true),
                FilterOperator.Has => Contains(expression, Scalar(field)),
                FilterOperator.HasAny => AnyContains(expression, Set(field), requireAll: false, negate: false),
                FilterOperator.HasAll => AnyContains(expression, Set(field), requireAll: true, negate: false),
                FilterOperator.HasNone => AnyContains(expression, Set(field), requireAll: false, negate: true),
                FilterOperator.Size => Size(expression, Scalar(field), field),
                FilterOperator.Exists => Exists(expression, Scalar(field)),
                _ => throw Unsupported(field.Operator, field.Field.ToString(), "operator")
            };
        }

        private string Equal(string expression, object? value)
        {
            if (value is null)
                return $"({expression} IS NULL OR {expression} = 'null'::jsonb)";
            DemandComparable(value);
            var parameter = Bind(ToJson(value));
            return $"COALESCE({expression} = CAST(@{parameter} AS jsonb), FALSE)";
        }

        private string Compare(string expression, string operation, object? value, FieldFilter field)
        {
            if (value is null)
                return "FALSE";
            if (TryNumber(value, out var number))
            {
                var parameter = Bind(number);
                return $"COALESCE(CASE WHEN jsonb_typeof({expression}) = 'number' " +
                       $"THEN ({expression} #>> '{{}}')::numeric {operation} @{parameter} ELSE FALSE END, FALSE)";
            }
            if (value is bool boolean)
            {
                var parameter = Bind(boolean);
                return $"COALESCE(CASE WHEN jsonb_typeof({expression}) = 'boolean' " +
                       $"THEN ({expression} #>> '{{}}')::boolean {operation} @{parameter} ELSE FALSE END, FALSE)";
            }
            if (TryOrderedTag(value, out var type, out var order))
            {
                var typeParameter = Bind(type);
                var valueParameter = Bind(order);
                return $"COALESCE(CASE WHEN {expression} ->> '{TypeTag}' = @{typeParameter} " +
                       $"THEN ({expression} ->> '{TaggedValue}')::numeric {operation} @{valueParameter} " +
                       "ELSE FALSE END, FALSE)";
            }
            if (TryText(value, out var text))
            {
                var parameter = Bind(text);
                return $"COALESCE(jsonb_typeof({expression}) = 'string' AND (({expression} #>> '{{}}') COLLATE \"C\" {operation} @{parameter}), FALSE)";
            }
            throw Unsupported(field.Operator, field.Field.ToString(), $"comparison value type '{value.GetType().FullName}'");
        }

        private string AnyEqual(string expression, IReadOnlyList<object?> values, bool negate)
        {
            var match = values.Count == 0
                ? "FALSE"
                : $"({string.Join(" OR ", values.Select(value => Equal(expression, value)))})";
            return negate ? $"(NOT {match})" : match;
        }

        private string Contains(string expression, object? value)
        {
            if (value is not null) DemandComparable(value);
            var parameter = Bind(ToJson(value));
            return $"COALESCE(jsonb_typeof({expression}) = 'array' AND {expression} @> jsonb_build_array(CAST(@{parameter} AS jsonb)), FALSE)";
        }

        private string AnyContains(
            string expression,
            IReadOnlyList<object?> values,
            bool requireAll,
            bool negate)
        {
            var identity = requireAll ? "TRUE" : "FALSE";
            var operation = requireAll ? " AND " : " OR ";
            var match = values.Count == 0
                ? identity
                : $"({string.Join(operation, values.Select(value => Contains(expression, value)))})";
            return negate ? $"(NOT {match})" : match;
        }

        private string Size(string expression, object? value, FieldFilter field)
        {
            int expected;
            try
            {
                expected = value is null
                    ? 0
                    : TryNumber(value, out var number)
                        ? (int)number
                        : 0;
            }
            catch (OverflowException)
            {
                throw Unsupported(field.Operator, field.Field.ToString(), "out-of-range size");
            }
            var parameter = Bind(expected);
            return $"(CASE WHEN jsonb_typeof({expression}) = 'array' THEN jsonb_array_length({expression}) ELSE 0 END = @{parameter})";
        }

        private static string Exists(string expression, object? value)
        {
            var present = value is not bool expected || expected;
            var exists = $"({expression} IS NOT NULL AND {expression} <> 'null'::jsonb)";
            return present ? exists : $"(NOT {exists})";
        }

        private string Bind(object value)
        {
            var name = $"pf{_parameter++}";
            command.Parameters.AddWithValue(name, value);
            return name;
        }
    }

    private static object? Scalar(FieldFilter filter) => filter.Value switch
    {
        FilterValue.Scalar scalar => scalar.Value,
        FilterValue.Set set when set.Values.Count > 0 => set.Values[0],
        _ => null
    };

    private static IReadOnlyList<object?> Set(FieldFilter filter) => filter.Value switch
    {
        FilterValue.Set set => set.Values,
        FilterValue.Scalar scalar => [scalar.Value],
        _ => []
    };

    private static bool TryNumber(object value, out decimal number)
    {
        switch (value)
        {
            case float item when float.IsFinite(item):
                number = Convert.ToDecimal(item, CultureInfo.InvariantCulture);
                return true;
            case double item when double.IsFinite(item):
                number = Convert.ToDecimal(item, CultureInfo.InvariantCulture);
                return true;
            case sbyte or byte or short or ushort or int or uint or long or ulong or decimal:
                number = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                return true;
            default:
                number = default;
                return false;
        }
    }

    private static bool TryText(object value, out string text)
    {
        text = value as string ?? string.Empty;
        return text.Length > 0 || value is string;
    }

    private static bool TryOrderedTag(object value, out string type, out long order)
    {
        (type, order) = value switch
        {
            DateOnly item => ("date", item.DayNumber),
            TimeOnly item => ("time", item.Ticks),
            DateTime item => ("datetime", item.Ticks),
            DateTimeOffset item => ("datetimeoffset", item.UtcDateTime.Ticks),
            TimeSpan item => ("timespan", item.Ticks),
            _ => (string.Empty, 0L)
        };
        return type.Length > 0;
    }

    private static void DemandComparable(object value)
    {
        if (value is byte[] or DataObject or DataArray)
            throw Unsupported(null, null,
                $"reference-identity metadata value type '{value.GetType().FullName}'");
    }

    private static string ToJson(object? value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer)) WriteValue(writer, value);
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static VectorFilterUnsupportedException Unsupported(
        FilterOperator? operation,
        string? field,
        string detail) =>
        new(Infrastructure.Constants.Provider.Name, operation, field,
            $"PgVector cannot faithfully push {detail}. Narrow the filter or select an adapter that declares it.");
}
