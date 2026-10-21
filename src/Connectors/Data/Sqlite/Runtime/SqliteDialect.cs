using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
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
    public string JsonArrayElementLike(string columnSql, string patternParameter, string literalParameter) =>
        // SQLite's LIKE folds ASCII case, which the case-sensitive floor does not; instr() is exact.
        $"EXISTS (SELECT 1 FROM json_each({columnSql}) WHERE instr(value, {literalParameter}) > 0)";

    /// <summary>
    /// SQLite's LIKE folds ASCII case, and the oracle compares ordinally, so scalar matching binds the raw literal and
    /// compares exactly: <c>substr</c> and <c>instr</c> yield values without a collation, so <c>=</c> is binary even over
    /// a column declared NOCASE. Wildcards in the value are literal by construction. A null value answers null, which
    /// filters as false.
    /// </summary>
    public string StringMatch(string columnSql, FilterOperator op, string value, Func<object?, string> bind)
    {
        var literal = bind(value);
        return op switch
        {
            FilterOperator.StartsWith => $"(substr({columnSql}, 1, length({literal})) = {literal})",
            // A start at or before the first character yields at most the whole value, which is shorter than a longer
            // literal and so never equal to it; an empty literal starts past the end and matches every non-null value.
            FilterOperator.EndsWith => $"(substr({columnSql}, length({columnSql}) - length({literal}) + 1) = {literal})",
            FilterOperator.Contains => $"(instr({columnSql}, {literal}) > 0)",
            _ => throw new NotSupportedException($"Operator '{op}' is not a scalar string match.")
        };
    }

    public string Read(PhysicalPath path, MappingValueShape shape, Type physicalType)
    {
        var root = $"koan_row.{Quote(path.Name)}";
        if (!path.IsNested) return root;
        var extracted = $"json_extract({root}, '{JsonPath(path.Segments)}')";
        var type = Nullable.GetUnderlyingType(physicalType) ?? physicalType;
        return IsNumeric(type) || type == typeof(bool) || type == typeof(TimeSpan)
            ? $"CAST({extracted} AS NUMERIC)"
            : extracted;
    }

    public string? JsonArrayOrderTerm(
        string arraySql,
        IReadOnlyList<string> elementSegments,
        bool max,
        bool descending,
        Type elementValueType)
    {
        var extracted = $"json_extract(koan_element.value, '{JsonPath(elementSegments)}')";
        var type = Nullable.GetUnderlyingType(elementValueType) ?? elementValueType;
        var value = IsNumeric(type) || type == typeof(bool) || type == typeof(TimeSpan)
            ? $"CAST({extracted} AS NUMERIC)"
            : extracted;
        if (type.IsEnum) value = RelationalEnumOrder.Rank(value, type);
        // json_each rejects a scalar, and a document may hold no array at that path at all, so the type is
        // checked rather than assumed. No rows means NULL, which sorts first — where the in-memory sorter
        // puts a widget with no sightings.
        var array = $"CASE WHEN json_type({arraySql}) = 'array' THEN {arraySql} ELSE '[]' END";
        var aggregate = $"(SELECT {(max ? "MAX" : "MIN")}({value}) FROM json_each({array}) AS koan_element)";
        // SQLite sorts NULL first ascending and last descending, which is where the framework's sorter puts
        // it, so the direction alone is enough.
        return descending ? $"{aggregate} DESC" : $"{aggregate} ASC";
    }

    internal static string Quote(string identifier) =>
        $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    internal static string JsonPath(IEnumerable<string> segments) => "$" + string.Concat(segments.Select(segment =>
        ".\"" + segment.Replace("\"", "\\\"", StringComparison.Ordinal) + "\""));

    private static bool IsNumeric(Type type) =>
        type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort) ||
        type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong) ||
        type == typeof(float) || type == typeof(double) || type == typeof(decimal);
}
