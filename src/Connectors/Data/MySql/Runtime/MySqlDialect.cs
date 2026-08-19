using Koan.Data.Abstractions;
using Koan.Data.Relational.Mapping;

namespace Koan.Data.Connector.MySql.Runtime;

internal sealed class MySqlDialect : IRelationalMappingDialect
{
    public string Read(PhysicalPath path, MappingValueShape shape, Type physicalType)
    {
        var root = Quote(path.Name);
        if (!path.IsNested) return root;
        var extracted = $"JSON_EXTRACT({root}, '{JsonPath(path.Segments)}')";
        if (shape == MappingValueShape.Object || IsCollection(physicalType)) return extracted;
        return Cast($"JSON_UNQUOTE({extracted})", physicalType);
    }

    public string QuoteIdent(string ident) => Quote(ident);
    public string EscapeLike(string fragment) => fragment.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("%", "\\%", StringComparison.Ordinal)
        .Replace("_", "\\_", StringComparison.Ordinal);
    public string Parameter(int index) => $"@p{index}";
    public string JsonArrayContains(string columnSql, string parameter) =>
        $"COALESCE(JSON_CONTAINS({columnSql}, JSON_ARRAY({parameter})), 0)";
    public string JsonArrayLength(string columnSql) => $"COALESCE(JSON_LENGTH({columnSql}), 0)";

    public static string Quote(string value) => $"`{value.Replace("`", "``", StringComparison.Ordinal)}`";

    public static string JsonPath(IEnumerable<string> segments)
    {
        var path = "$" + string.Concat(segments.Select(segment => ".\"" +
            segment.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal) + "\""));
        return path.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("'", "''", StringComparison.Ordinal);
    }

    internal static string Cast(string expression, Type type)
    {
        var value = Nullable.GetUnderlyingType(type) ?? type;
        if (value.IsEnum) value = Enum.GetUnderlyingType(value);
        if (value == typeof(bool))
            return $"CASE LOWER({expression}) WHEN 'true' THEN 1 WHEN 'false' THEN 0 ELSE CAST({expression} AS UNSIGNED) END";
        if (value == typeof(byte) || value == typeof(ushort) ||
            value == typeof(uint) || value == typeof(ulong)) return $"CAST({expression} AS UNSIGNED)";
        if (value == typeof(sbyte) || value == typeof(short) || value == typeof(int) ||
            value == typeof(long) || value == typeof(TimeSpan)) return $"CAST({expression} AS SIGNED)";
        if (value == typeof(float) || value == typeof(double) || value == typeof(decimal))
            return $"CAST({expression} AS DECIMAL(65, 30))";
        return expression;
    }

    private static bool IsCollection(Type type) =>
        type != typeof(string) && type != typeof(byte[]) && typeof(System.Collections.IEnumerable).IsAssignableFrom(type);
}
