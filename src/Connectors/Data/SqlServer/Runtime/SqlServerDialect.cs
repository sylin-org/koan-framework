using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Relational.Linq;
using Koan.Data.Relational.Mapping;

namespace Koan.Data.Connector.SqlServer.Runtime;

internal sealed class SqlServerDialect : IRelationalMappingDialect
{
    public string Read(PhysicalPath path, MappingValueShape shape, Type physicalType)
    {
        var root = Quote(path.Name);
        if (!path.IsNested) return root;
        var jsonPath = JsonPath(path.Segments);
        if (shape == MappingValueShape.Object || IsCollection(physicalType))
            return $"JSON_QUERY({root}, '{jsonPath}')";
        return Cast($"JSON_VALUE({root}, '{jsonPath}')", physicalType);
    }

    public string? JsonArrayOrderTerm(
        string arraySql,
        IReadOnlyList<string> elementSegments,
        bool max,
        bool descending,
        Type elementValueType)
    {
        var value = Cast($"JSON_VALUE(koan_element.[value], '{JsonPath(elementSegments)}')", elementValueType);
        if ((Nullable.GetUnderlyingType(elementValueType) ?? elementValueType).IsEnum)
            value = RelationalEnumOrder.Rank(value, elementValueType);
        // JSON_QUERY already returns NULL for anything that is not an object or array, so an absent path
        // becomes an empty array rather than an error. No rows means NULL, which sorts first.
        var aggregate = $"(SELECT {(max ? "MAX" : "MIN")}({value}) FROM OPENJSON(ISNULL({arraySql}, N'[]')) AS koan_element)";
        // SQL Server sorts NULL first ascending and last descending, which is where the framework's sorter
        // puts it, so the direction alone is enough.
        return descending ? $"{aggregate} DESC" : $"{aggregate} ASC";
    }

    public string QuoteIdent(string ident) => Quote(ident);
    public string EscapeLike(string fragment) => fragment.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");
    public string Parameter(int index) => $"@p{index}";

    /// <summary>
    /// SQL Server plans a short IN list best, so a set binds a parameter per member while the statement stays within
    /// <see cref="MaximumParameters"/>, and the translator moves the largest set to <see cref="MemberOf"/> when it does
    /// not (DATA-0162).
    /// </summary>
    public bool PrefersSetParameters => true;

    /// <summary>SqlClient sends at most 2,098 parameters in a statement (measured: 2,099 is refused).</summary>
    public int MaximumParameters => Infrastructure.Constants.StatementParameters;

    /// <summary>
    /// A set binds as one JSON text whose members read as the type a lone parameter of them binds as
    /// (<see cref="SqlServerJsonSet"/>), collation included, so In answers what Eq answers. Members of mixed types, or of a
    /// type it does not read, decline.
    /// </summary>
    public string? MemberOf(string columnSql, IReadOnlyList<object?> members, bool negate, Func<object?, string> bind)
    {
        if (SqlServerJsonSet.Select(members, bind) is not { } set) return null;
        // The members read as nullable, and NOT IN over a nullable set rescans it per row. A null column is the caller's.
        return negate
            ? $"NOT EXISTS (SELECT 1 FROM ({set}) koan_set(koan_value) WHERE koan_set.koan_value = {columnSql})"
            : $"{columnSql} IN ({set})";
    }

    /// <summary>
    /// A JSON array column against a set bound as one JSON text: some element is a member (<see cref="SetMatch.Any"/>),
    /// every member is an element (<see cref="SetMatch.All"/>), or no element is a member (<see cref="SetMatch.None"/>).
    /// Elements are read and compared as <see cref="JsonArrayContains(string, string, Type)"/> compares one.
    /// </summary>
    public string? CollectionMemberOf(string columnSql, IReadOnlyList<object?> members, SetMatch match, Type elementType,
        Func<object?, string> bind)
    {
        if (SqlServerJsonSet.Select(members, bind) is not { } set) return null;
        var element = Element(elementType);
        var any = $"EXISTS (SELECT 1 FROM OPENJSON({columnSql}) item WHERE {element} IN ({set}))";
        return match switch
        {
            SetMatch.Any => any,
            SetMatch.None => $"(NOT {any})",
            // Every member is an element: no member is outside the array's non-null elements, an anti-join the server
            // hashes rather than rescanning the array per member.
            _ => $"(NOT EXISTS (SELECT 1 FROM ({set}) koan_set(koan_value) " +
                 $"WHERE koan_set.koan_value NOT IN (SELECT {element} FROM OPENJSON({columnSql}) item WHERE {element} IS NOT NULL)))"
        };
    }

    /// <summary>
    /// JSON holds a Guid as lower-case D text and a DateTime as round-trip ISO text, so both sides compare as that text: a
    /// DateTime operand is rewritten to fixed-width wall-clock text, and the comparand is encoded to match (DATA-0139).
    /// </summary>
    public string DocumentOperand(string readSql, Type physicalType) =>
        RelationalDocumentText.IsDateTime(physicalType) ? DateTimeOperand(readSql) : readSql;

    public object? DocumentComparand(object? value, Type physicalType) => RelationalDocumentText.Comparand(value);

    // RelationalDocumentText.DateTimeOperand in T-SQL: the first nineteen characters, then the fraction padded to seven
    // digits, dropping a Z or ±hh:mm suffix. NULL stays NULL.
    private static string DateTimeOperand(string text)
    {
        var zone = $"(CASE WHEN RIGHT({text}, 1) = 'Z' THEN 1 " +
                   $"WHEN SUBSTRING({text}, LEN({text}) - 5, 1) IN ('+', '-') THEN 6 ELSE 0 END)";
        var fraction = $"(CASE WHEN SUBSTRING({text}, 20, 1) = '.' " +
                       $"THEN SUBSTRING({text}, 21, LEN({text}) - 20 - {zone}) ELSE '' END)";
        return $"(LEFT({text}, 19) + '.' + LEFT({fraction} + '0000000', 7))";
    }

    public string JsonArrayContains(string columnSql, string parameter) => JsonArrayContains(columnSql, parameter, typeof(string));

    /// <summary>
    /// Each element is read exactly as a scalar read out of a document is — converted to its type, a DateTime rewritten
    /// to wall-clock text — and compared with a comparand encoded to match (DATA-0139), so <c>Has</c> answers what
    /// <c>Eq</c> answers. A decimal or a real number converts to decimal(38, 10), as the scalar comparison does: it
    /// meets a member by value to ten places.
    /// </summary>
    public string JsonArrayContains(string columnSql, string parameter, Type elementType) =>
        $"EXISTS (SELECT 1 FROM OPENJSON({columnSql}) item WHERE {Element(elementType)} = {parameter})";

    public object? JsonArrayComparand(object? value, Type elementType) => RelationalDocumentText.Comparand(value);

    private string Element(Type elementType) => DocumentOperand(Cast("item.[value]", elementType), elementType);
    public string JsonArrayLength(string columnSql) => $"(SELECT COUNT(1) FROM OPENJSON({columnSql}))";
    /// <summary>
    /// A scalar string match under the column's collation.
    /// The bracket escapes of <see cref="EscapeLike"/> need no ESCAPE clause, and one would swallow a literal backslash.
    /// </summary>
    public string StringMatch(string columnSql, FilterOperator op, string value, Func<object?, string> bind)
    {
        var escaped = EscapeLike(value);
        var pattern = op switch
        {
            FilterOperator.StartsWith => escaped + "%",
            FilterOperator.EndsWith => "%" + escaped,
            FilterOperator.Contains => "%" + escaped + "%",
            _ => throw new NotSupportedException($"Operator '{op}' is not a scalar string match.")
        };
        return $"({columnSql} LIKE {bind(pattern)})";
    }

    public string JsonArrayElementLike(string columnSql, string patternParameter, string literalParameter) =>
        // This dialect escapes LIKE metacharacters by bracket-quoting ([%], [_], [[]), which needs no
        // ESCAPE declaration — and declaring one would make the escape swallow a literal backslash in
        // the pattern. The default server collation folds case, so the comparison is forced binary to
        // answer what the case-sensitive floor answers; ISNULL mirrors JsonArrayOrderTerm's null guard.
        $"EXISTS (SELECT 1 FROM OPENJSON(ISNULL({columnSql}, N'[]')) item WHERE item.[value] COLLATE Latin1_General_BIN2 LIKE {patternParameter})";

    public static string Quote(string value) => $"[{value.Replace("]", "]]", StringComparison.Ordinal)}]";

    public static string JsonPath(IEnumerable<string> segments) =>
        "$" + string.Concat(segments.Select(segment => ".\"" + segment.Replace("\"", "\\\"", StringComparison.Ordinal) + "\""));

    private static string Cast(string expression, Type type)
    {
        var value = Nullable.GetUnderlyingType(type) ?? type;
        if (value.IsEnum) value = typeof(string);
        if (value == typeof(bool)) return $"TRY_CONVERT(bit, {expression})";
        if (value == typeof(byte) || value == typeof(sbyte) || value == typeof(short) || value == typeof(ushort) ||
            value == typeof(int) || value == typeof(uint) || value == typeof(long) || value == typeof(ulong) ||
            value == typeof(TimeSpan)) return $"TRY_CONVERT(bigint, {expression})";
        // A document holds a double's small or large values in exponent form, which text converts to float but not to
        // decimal (DATA-0164).
        if (value == typeof(float) || value == typeof(double) || value == typeof(decimal))
            return $"COALESCE(TRY_CONVERT(decimal(38, 10), {expression}), TRY_CONVERT(decimal(38, 10), TRY_CONVERT(float, {expression})))";
        return expression;
    }

    private static bool IsCollection(Type type) =>
        type != typeof(string) && type != typeof(byte[]) && typeof(System.Collections.IEnumerable).IsAssignableFrom(type);
}
