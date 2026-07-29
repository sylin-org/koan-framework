using System.Globalization;
using System.Text;
using System.Text.Json;
using Koan.Data.Abstractions.Filtering;

namespace Koan.Data.Vector.Connector.Milvus;

/// <summary>Exact Milvus JSON pre-filter writer.</summary>
internal static class MilvusFilter
{
    internal static readonly FilterSupport Capabilities = FilterSupport.Uniform(
        nestedPaths: true,
        ignoreCase: false,
        FilterOperator.Eq,
        FilterOperator.Ne,
        FilterOperator.In,
        FilterOperator.Nin,
        FilterOperator.Has,
        FilterOperator.HasAny,
        FilterOperator.HasAll,
        FilterOperator.HasNone,
        FilterOperator.Exists);

    internal static string Write(Filter filter) => WriteNode(filter, negate: false);

    private static string WriteNode(Filter filter, bool negate) => filter switch
    {
        AllOf all => Group(negate ? "or" : "and", all.Operands.Select(item => WriteNode(item, negate))),
        AnyOf any => Group(negate ? "and" : "or", any.Operands.Select(item => WriteNode(item, negate))),
        Not not => WriteNode(not.Operand, !negate),
        FieldFilter field => WriteLeaf(field, negate),
        _ => throw Unsupported(null, null, $"filter node '{filter.GetType().Name}'")
    };

    private static string WriteLeaf(FieldFilter field, bool negate)
    {
        if (field.IgnoreCase)
            throw Unsupported(field.Operator, field.Field.ToString(), "case-insensitive comparison");
        var path = Path(field.Field.Segments);
        var scalar = Scalar(field);
        var set = Set(field);
        return (field.Operator, negate) switch
        {
            (FilterOperator.Eq, false) when scalar is null => $"{path} is null",
            (FilterOperator.Eq, true) when scalar is null => $"{path} is not null",
            (FilterOperator.Eq, false) => $"{path} == {Value(scalar)}",
            (FilterOperator.Eq, true) => $"({path} is null or {path} != {Value(scalar)})",
            (FilterOperator.Ne, false) when scalar is null => $"{path} is not null",
            (FilterOperator.Ne, true) when scalar is null => $"{path} is null",
            (FilterOperator.Ne, false) => $"({path} is null or {path} != {Value(scalar)})",
            (FilterOperator.Ne, true) => $"{path} == {Value(scalar)}",
            (FilterOperator.In, false) => $"{path} in {Array(set, field)}",
            (FilterOperator.In, true) => $"({path} is null or {path} not in {Array(set, field)})",
            (FilterOperator.Nin, false) => $"({path} is null or {path} not in {Array(set, field)})",
            (FilterOperator.Nin, true) => $"{path} in {Array(set, field)}",
            (FilterOperator.Has, false) => $"json_contains({path}, {Value(scalar)})",
            (FilterOperator.Has, true) => $"({path} is null or not json_contains({path}, {Value(scalar)}))",
            (FilterOperator.HasAny, false) => $"json_contains_any({path}, {Array(set, field)})",
            (FilterOperator.HasAny, true) => $"({path} is null or not json_contains_any({path}, {Array(set, field)}))",
            (FilterOperator.HasAll, false) => $"json_contains_all({path}, {Array(set, field)})",
            (FilterOperator.HasAll, true) => $"({path} is null or not json_contains_all({path}, {Array(set, field)}))",
            (FilterOperator.HasNone, false) => $"({path} is null or not json_contains_any({path}, {Array(set, field)}))",
            (FilterOperator.HasNone, true) => $"json_contains_any({path}, {Array(set, field)})",
            (FilterOperator.Exists, _) => Exists(path, scalar, negate),
            _ => throw Unsupported(field.Operator, field.Field.ToString(), "operator")
        };
    }

    private static string Exists(string path, object? scalar, bool negate)
    {
        var present = scalar is not bool desired || desired;
        if (negate) present = !present;
        return present ? $"{path} is not null" : $"{path} is null";
    }

    private static string Group(string operation, IEnumerable<string> operands)
    {
        var values = operands.ToArray();
        if (values.Length == 0)
            throw Unsupported(null, null, "empty boolean filter group");
        return values.Length == 1 ? values[0] : $"({string.Join($" {operation} ", values)})";
    }

    private static string Path(IReadOnlyList<string> segments)
    {
        if (segments.Count == 0 || segments.Any(string.IsNullOrWhiteSpace))
            throw Unsupported(null, string.Join('.', segments), "empty metadata path");
        var output = new StringBuilder(Infrastructure.Constants.Wire.Metadata);
        foreach (var segment in segments)
            output.Append("[\"").Append(Escape(segment)).Append("\"]");
        return output.ToString();
    }

    private static string Array(IReadOnlyList<object?> values, FieldFilter field)
    {
        if (values.Count == 0)
            throw Unsupported(field.Operator, field.Field.ToString(), "empty filter value set");
        return "[" + string.Join(',', values.Select(Value)) + "]";
    }

    private static string Value(object? value) => value switch
    {
        null => "null",
        string text => JsonSerializer.Serialize(text),
        bool boolean => boolean ? "true" : "false",
        sbyte or byte or short or ushort or int or uint or long or ulong or decimal =>
            Convert.ToString(value, CultureInfo.InvariantCulture)!,
        float number when float.IsFinite(number) => number.ToString("R", CultureInfo.InvariantCulture),
        double number when double.IsFinite(number) => number.ToString("R", CultureInfo.InvariantCulture),
        Guid guid => JsonSerializer.Serialize(guid.ToString("D")),
        DateOnly date => JsonSerializer.Serialize(date.ToString("O", CultureInfo.InvariantCulture)),
        TimeOnly time => JsonSerializer.Serialize(time.ToString("O", CultureInfo.InvariantCulture)),
        DateTime date => JsonSerializer.Serialize(date.ToString("O", CultureInfo.InvariantCulture)),
        DateTimeOffset date => JsonSerializer.Serialize(date.ToString("O", CultureInfo.InvariantCulture)),
        TimeSpan duration => JsonSerializer.Serialize(duration.ToString("c", CultureInfo.InvariantCulture)),
        byte[] bytes => JsonSerializer.Serialize(Convert.ToBase64String(bytes)),
        _ => throw Unsupported(null, null, $"filter value type '{value.GetType().FullName}'")
    };

    private static object? Scalar(FieldFilter field) => field.Value switch
    {
        FilterValue.Scalar scalar => scalar.Value,
        FilterValue.Set set when set.Values.Count > 0 => set.Values[0],
        FilterValue.None => null,
        _ => null
    };

    private static IReadOnlyList<object?> Set(FieldFilter field) => field.Value switch
    {
        FilterValue.Set set => set.Values,
        FilterValue.Scalar scalar => [scalar.Value],
        _ => []
    };

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static VectorFilterUnsupportedException Unsupported(
        FilterOperator? operation,
        string? field,
        string detail) => new(
        Infrastructure.Constants.Provider.Name,
        operation,
        field,
        $"Milvus cannot faithfully push {detail}. Narrow the filter or select an adapter that declares it.");
}
