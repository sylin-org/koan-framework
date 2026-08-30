using System.Globalization;
using System.Text.Json;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Vector;

namespace Koan.Data.Vector.Connector.Chroma;

internal static class ChromaFilter
{
    // Chroma where-clauses are single-key operator dicts or $and/$or groups; there is no $nor, no
    // exists, no array operators, and range comparisons accept numeric values only. Not cannot be
    // expressed (complements diverge on absent keys), so negation stays undeclared.
    internal static readonly FilterSupport Capabilities = FilterSupport.Uniform(
        nestedPaths: false,
        ignoreCase: false,
        FilterOperator.Eq, FilterOperator.Ne,
        FilterOperator.Gt, FilterOperator.Gte, FilterOperator.Lt, FilterOperator.Lte,
        FilterOperator.In, FilterOperator.Nin) with { SupportsNegation = false };

    internal static void Write(Utf8JsonWriter writer, Filter? filter)
    {
        if (filter is null)
            throw Unsupported(null, null, "a null filter");
        writer.WriteStartObject();
        WriteCondition(writer, filter);
        writer.WriteEndObject();
    }

    /// <summary>Writes the flat Chroma metadata projection of one point's neutral metadata: the
    /// bookkeeping keys, the neutral JSON blob, and one converted scalar per top-level property.
    /// Container values (objects/arrays) and nulls stay in the blob only — Chroma cannot match them.</summary>
    internal static void WritePointMetadata(Utf8JsonWriter writer, string key, string scope, string? metadataJson, DataObject? metadata)
    {
        writer.WriteStartObject();
        writer.WriteString(Infrastructure.Constants.Wire.Id, key);
        writer.WriteString(Infrastructure.Constants.Wire.Scope, scope);
        if (metadataJson is not null)
            writer.WriteString(Infrastructure.Constants.Wire.Metadata, metadataJson);
        if (metadata is not null)
            foreach (var property in metadata.Properties)
            {
                if (property.Value is null or DataObject or DataArray) continue;
                writer.WritePropertyName(Infrastructure.Constants.Wire.Index + "." + property.Name);
                WriteIndexValue(writer, property.Value);
            }
        writer.WriteEndObject();
    }

    private static void WriteCondition(Utf8JsonWriter writer, Filter filter)
    {
        switch (filter)
        {
            case AllOf all:
                WriteGroup(writer, "$and", all.Operands);
                return;
            case AnyOf any:
                WriteGroup(writer, "$or", any.Operands);
                return;
            case Not:
                throw Unsupported(null, null, "a negation (Chroma has no $nor and complements diverge on absent keys)");
            case FieldFilter field:
                WriteLeaf(writer, field);
                return;
            default:
                throw Unsupported(null, null, $"filter node '{filter.GetType().Name}'");
        }
    }

    private static void WriteGroup(Utf8JsonWriter writer, string name, IReadOnlyList<Filter> operands)
    {
        if (operands.Count == 1)
        {
            WriteCondition(writer, operands[0]);
            return;
        }
        writer.WritePropertyName(name);
        writer.WriteStartArray();
        foreach (var operand in operands)
        {
            writer.WriteStartObject();
            WriteCondition(writer, operand);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static void WriteLeaf(Utf8JsonWriter writer, FieldFilter field)
    {
        if (field.IgnoreCase)
            throw Unsupported(field.Operator, field.Field.ToString(), "case-insensitive comparison");
        if (field.Field.Segments.Count != 1 || field.Field.Segments[0].Contains('.', StringComparison.Ordinal))
            throw Unsupported(field.Operator, field.Field.ToString(),
                "a nested metadata path (Chroma metadata is flat; dotted paths are literal keys that silently match nothing)");
        var key = Infrastructure.Constants.Wire.Index + "." + field.Field.Segments[0];
        switch (field.Operator)
        {
            case FilterOperator.Eq:
                if (Scalar(field) is null) throw Unsupported(field.Operator, key, "an equality against null");
                WriteOperator(writer, key, "$eq", Scalar(field));
                return;
            case FilterOperator.Ne:
                if (Scalar(field) is null) throw Unsupported(field.Operator, key, "an inequality against null");
                WriteOperator(writer, key, "$ne", Scalar(field));
                return;
            case FilterOperator.Gt:
            case FilterOperator.Gte:
            case FilterOperator.Lt:
            case FilterOperator.Lte:
                WriteRange(writer, key, OperatorName(field.Operator), field);
                return;
            case FilterOperator.In:
            case FilterOperator.Nin:
                WriteSet(writer, key, OperatorName(field.Operator), Set(field));
                return;
            default:
                throw Unsupported(field.Operator, field.Field.ToString(), "operator");
        }
    }

    private static void WriteOperator(Utf8JsonWriter writer, string key, string operation, object? value)
    {
        writer.WritePropertyName(key);
        writer.WriteStartObject();
        writer.WritePropertyName(operation);
        WriteIndexValue(writer, value);
        writer.WriteEndObject();
    }

    private static void WriteRange(Utf8JsonWriter writer, string key, string operation, FieldFilter field)
    {
        var value = Scalar(field);
        if (!IsIndexNumber(value))
            throw Unsupported(field.Operator, key,
                "a range comparison on a non-numeric value (Chroma range operators accept numbers only)");
        WriteOperator(writer, key, operation, value);
    }

    private static void WriteSet(Utf8JsonWriter writer, string key, string operation, IReadOnlyList<object?> values)
    {
        if (values.Any(static value => value is null))
            throw Unsupported(null, key, "a set comparison containing null");
        writer.WritePropertyName(key);
        writer.WriteStartObject();
        writer.WritePropertyName(operation);
        writer.WriteStartArray();
        foreach (var value in values) WriteIndexValue(writer, value);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static string OperatorName(FilterOperator operation) => operation switch
    {
        FilterOperator.Gt => "$gt",
        FilterOperator.Gte => "$gte",
        FilterOperator.Lt => "$lt",
        FilterOperator.Lte => "$lte",
        FilterOperator.In => "$in",
        FilterOperator.Nin => "$nin",
        _ => throw Unsupported(operation, null, "operator")
    };

    private static bool IsIndexNumber(object? value) => value is sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal;

    /// <summary>Converts one neutral value into the flat scalar Chroma metadata can hold and match.
    /// The identical conversion runs at write time (index projection) and filter time, so pushdown
    /// compares like with like.</summary>
    private static void WriteIndexValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
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
            default:
                throw Unsupported(null, null,
                    value is null ? "a null value" : $"value type '{value.GetType().FullName}'");
        }
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

    private static VectorFilterUnsupportedException Unsupported(FilterOperator? operation, string? field, string detail) =>
        new(Infrastructure.Constants.Provider.Name, operation, field,
            $"Chroma cannot faithfully push {detail}. Narrow the filter or select an adapter that declares it.");
}
