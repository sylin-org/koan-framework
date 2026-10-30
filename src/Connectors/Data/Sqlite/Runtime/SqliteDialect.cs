using System.Globalization;
using System.Text.Json;
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
    public string JsonArrayContains(string columnSql, string parameter) => JsonArrayContains(columnSql, parameter, typeof(string));

    /// <summary>
    /// Each element is read as a scalar read out of a document is — a DateTime rewritten to wall-clock text — and compared
    /// with a comparand encoded to match (DATA-0139): a Guid as its text, and a decimal as the real number json_each reads
    /// a JSON number as.
    /// </summary>
    public string JsonArrayContains(string columnSql, string parameter, Type elementType) =>
        $"EXISTS (SELECT 1 FROM json_each({columnSql}) WHERE {DocumentOperand("value", elementType)} = {parameter})";

    public object? JsonArrayComparand(object? value, Type elementType) => RelationalDocumentText.Comparand(value) switch
    {
        decimal number => (double)number,
        var other => other
    };
    public string JsonArrayLength(string columnSql) => $"json_array_length({columnSql})";

    /// <summary>
    /// A set binds as one JSON array matched through <c>json_each</c>, so its size meets no parameter limit
    /// (DATA-0162). Each member is written as the value Microsoft.Data.Sqlite binds for it, so <c>json_each</c> yields
    /// the same storage class and the same value a list of parameters would: an integer or a boolean as INTEGER, a real
    /// as REAL, and a string, Guid, date, decimal or char as the TEXT the driver writes. A member JSON cannot carry (a
    /// byte array, a non-finite real, an unsigned 64-bit integer) leaves the set on one parameter per member.
    /// </summary>
    public string? MemberOf(string columnSql, IReadOnlyList<object?> members, bool negate, Func<object?, string> bind)
        => JsonSet(members) is { } json
            ? $"{columnSql} {(negate ? "NOT IN" : "IN")} (SELECT value FROM json_each({bind(json)}))"
            : null;

    /// <summary>SQLite accepts 32,766 variables in a statement.</summary>
    public int MaximumParameters => 32_766;

    /// <summary>A JSON array column against a set bound as one JSON array, each element read and compared as
    /// <see cref="JsonArrayContains(string, string, Type)"/> compares one (DATA-0162).</summary>
    public string? CollectionMemberOf(string columnSql, IReadOnlyList<object?> members, SetMatch match, Type elementType,
        Func<object?, string> bind)
    {
        if (JsonSet(members) is not { } json) return null;
        var set = bind(json);
        var element = DocumentOperand("value", elementType);
        var any = $"EXISTS (SELECT 1 FROM json_each({columnSql}) WHERE {element} IN (SELECT value FROM json_each({set})))";
        return match switch
        {
            SetMatch.Any => any,
            SetMatch.None => $"(NOT {any})",
            _ => $"(NOT EXISTS (SELECT 1 FROM json_each({set}) AS koan_set " +
                 $"WHERE koan_set.value NOT IN (SELECT {element} FROM json_each({columnSql}) WHERE value IS NOT NULL)))"
        };
    }

    /// <summary>
    /// The members as one JSON array, each written as the value the driver binds for it, or null when one has no JSON
    /// form that <c>json_each</c> turns back into that value.
    /// </summary>
    internal static string? JsonSet(IReadOnlyList<object?> members)
    {
        if (members.Count == 0) return null;
        var json = new System.Text.StringBuilder(members.Count * 16).Append('[');
        for (var index = 0; index < members.Count; index++)
        {
            if (index > 0) json.Append(',');
            switch (members[index])
            {
                case string text: AppendString(json, text); break;
                case char character: AppendString(json, character.ToString()); break;
                case bool flag: json.Append(flag ? '1' : '0'); break;
                case long value: json.Append(value.ToString(CultureInfo.InvariantCulture)); break;
                case int value: json.Append(value.ToString(CultureInfo.InvariantCulture)); break;
                case short value: json.Append(value.ToString(CultureInfo.InvariantCulture)); break;
                case byte value: json.Append(value.ToString(CultureInfo.InvariantCulture)); break;
                case sbyte value: json.Append(value.ToString(CultureInfo.InvariantCulture)); break;
                case ushort value: json.Append(value.ToString(CultureInfo.InvariantCulture)); break;
                case uint value: json.Append(value.ToString(CultureInfo.InvariantCulture)); break;
                case double value when double.IsFinite(value): AppendReal(json, value); break;
                case float value when float.IsFinite(value): AppendReal(json, value); break;
                // The driver binds these as TEXT in these exact forms.
                case Guid value: AppendString(json, value.ToString().ToUpperInvariant()); break;
                case decimal value: AppendString(json, value.ToString("0.0###########################", CultureInfo.InvariantCulture)); break;
                case DateTime value: AppendString(json, value.ToString(@"yyyy\-MM\-dd HH\:mm\:ss.FFFFFFF", CultureInfo.InvariantCulture).TrimEnd('.')); break;
                case DateOnly value: AppendString(json, value.ToString(@"yyyy\-MM\-dd", CultureInfo.InvariantCulture)); break;
                default: return null;
            }
        }
        return json.Append(']').ToString();
    }

    // A whole real keeps its decimal point, so json_each reads it back as REAL, as the bound parameter is.
    private static void AppendReal(System.Text.StringBuilder json, double value)
    {
        var text = value.ToString("R", CultureInfo.InvariantCulture);
        json.Append(text);
        if (text.IndexOfAny(['.', 'E', 'e']) < 0) json.Append(".0");
    }

    private static void AppendString(System.Text.StringBuilder json, string value) =>
        json.Append(JsonSerializer.Serialize(value));
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

    /// <summary>
    /// JSON holds a Guid as lower-case D text and a DateTime as round-trip ISO text, so both sides compare as that
    /// text: a DateTime operand is rewritten to fixed-width wall-clock text, and the comparand is encoded to match
    /// (DATA-0139). Index expressions keep <see cref="Read"/>.
    /// </summary>
    public string DocumentOperand(string readSql, Type physicalType) =>
        RelationalDocumentText.IsDateTime(physicalType) ? RelationalDocumentText.DateTimeOperand(readSql) : readSql;

    public object? DocumentComparand(object? value, Type physicalType) => RelationalDocumentText.Comparand(value);

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
