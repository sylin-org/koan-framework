using Koan.Data.Abstractions;
using Koan.Data.Relational.Linq;
using Koan.Data.Relational.Mapping;

namespace Koan.Data.Connector.Sqlite.Runtime;

internal sealed class SqliteDialect : IRelationalMappingDialect
{
    public string QuoteIdent(string ident) => Quote(ident);
    public string EscapeLike(string fragment) => fragment.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("%", "\\%", StringComparison.Ordinal)
        .Replace("_", "\\_", StringComparison.Ordinal);
    public string Parameter(int index) => $"@p{index}";
    public string JsonArrayContains(string columnSql, string parameter) =>
        $"EXISTS (SELECT 1 FROM json_each({columnSql}) WHERE value = {parameter})";
    public string JsonArrayLength(string columnSql) => $"json_array_length({columnSql})";

    public string Read(PhysicalPath path, MappingValueShape shape, Type physicalType)
    {
        var root = $"koan_row.{Quote(path.Name)}";
        if (!path.IsNested) return root;
        var jsonPath = "$" + string.Concat(path.Segments.Select(static segment =>
            ".\"" + segment.Replace("\"", "\\\"", StringComparison.Ordinal) + "\""));
        var extracted = $"json_extract({root}, '{jsonPath.Replace("'", "''", StringComparison.Ordinal)}')";
        var type = Nullable.GetUnderlyingType(physicalType) ?? physicalType;
        return IsNumeric(type) || type == typeof(bool) || type == typeof(TimeSpan)
            ? $"CAST({extracted} AS NUMERIC)"
            : extracted;
    }

    internal static string Quote(string identifier) =>
        $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static bool IsNumeric(Type type) =>
        type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort) ||
        type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong) ||
        type == typeof(float) || type == typeof(double) || type == typeof(decimal);
}
