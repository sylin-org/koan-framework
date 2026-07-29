using Koan.Data.Abstractions;
using Koan.Data.Relational.Mapping;

namespace Koan.Data.Relational.Npgsql.Runtime;

internal sealed class NpgsqlDialect : IRelationalMappingDialect
{
    public string Read(PhysicalPath path, MappingValueShape shape, Type physicalType)
    {
        var root = Quote(path.Name);
        if (!path.IsNested) return root;
        var literal = "'{" + string.Join(',', path.Segments.Select(EscapePath)) + "}'";
        if (shape == MappingValueShape.Object || IsCollection(physicalType)) return $"({root} #> {literal})";
        return Cast($"({root} #>> {literal})", physicalType);
    }

    public string QuoteIdent(string ident) => Quote(ident);
    public string EscapeLike(string fragment) => fragment.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
    public string Parameter(int index) => $"@p{index}";
    public string JsonArrayContains(string columnSql, string parameter) =>
        $"EXISTS (SELECT 1 FROM jsonb_array_elements_text({columnSql}) item WHERE item = {parameter})";
    public string JsonArrayLength(string columnSql) => $"jsonb_array_length({columnSql})";

    public static string Quote(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
    public static string EscapePath(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string Cast(string expression, Type type)
    {
        var effective = Nullable.GetUnderlyingType(type) ?? type;
        if (effective.IsEnum) effective = Enum.GetUnderlyingType(effective);
        if (effective == typeof(bool)) return $"({expression})::boolean";
        if (effective == typeof(byte) || effective == typeof(sbyte) || effective == typeof(short) ||
            effective == typeof(ushort) || effective == typeof(int) || effective == typeof(uint) ||
            effective == typeof(long) || effective == typeof(ulong) || effective == typeof(TimeSpan))
            return $"({expression})::bigint";
        if (effective == typeof(float) || effective == typeof(double) || effective == typeof(decimal))
            return $"({expression})::numeric";
        return expression;
    }

    private static bool IsCollection(Type type) =>
        type != typeof(string) && type != typeof(byte[]) && typeof(System.Collections.IEnumerable).IsAssignableFrom(type);
}
