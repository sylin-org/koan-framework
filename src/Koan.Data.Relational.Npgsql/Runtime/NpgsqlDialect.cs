using Koan.Data.Abstractions;
using Koan.Data.Relational.Ado;
using Koan.Data.Relational.Linq;
using Koan.Data.Relational.Mapping;

namespace Koan.Data.Relational.Npgsql.Runtime;

internal sealed class NpgsqlDialect : IRelationalMappingDialect
{
    /// <summary>
    /// JSON holds a Guid as lower-case D text and a DateTime as round-trip ISO text, so both sides compare as that
    /// text: a DateTime operand is rewritten to fixed-width wall-clock text, and the comparand is encoded to match
    /// (DATA-0139). Index expressions keep <see cref="Read"/>.
    /// </summary>
    public string DocumentOperand(string readSql, Type physicalType) =>
        RelationalDocumentText.IsDateTime(physicalType) ? RelationalDocumentText.DateTimeOperand(readSql) + " COLLATE \"C\"" : readSql;

    public object? DocumentComparand(object? value, Type physicalType) => RelationalDocumentText.Comparand(value);

    public string Read(PhysicalPath path, MappingValueShape shape, Type physicalType)
    {
        var root = Quote(path.Name);
        if (!path.IsNested) return root;
        var literal = "'{" + string.Join(',', path.Segments.Select(EscapePath)) + "}'";
        if (shape == MappingValueShape.Object || IsCollection(physicalType)) return $"({root} #> {literal})";
        return Cast($"({root} #>> {literal})", physicalType);
    }

    public string? JsonArrayOrderTerm(
        string arraySql,
        IReadOnlyList<string> elementSegments,
        bool max,
        bool descending,
        Type elementValueType)
    {
        var literal = "'{" + string.Join(',', elementSegments.Select(EscapePath)) + "}'";
        var value = Cast($"(koan_element.value #>> {literal})", elementValueType);
        if ((Nullable.GetUnderlyingType(elementValueType) ?? elementValueType).IsEnum)
            value = RelationalEnumOrder.Rank(value, elementValueType);
        // A document may hold no array at that path at all, and jsonb_array_elements refuses a scalar, so the
        // type is checked rather than assumed. An absent or empty array yields NULL, which sorts first —
        // the same place the in-memory sorter puts a widget with no sightings.
        var array = $"CASE WHEN jsonb_typeof({arraySql}) = 'array' THEN {arraySql} ELSE '[]'::jsonb END";
        var aggregate = $"(SELECT {(max ? "MAX" : "MIN")}({value}) FROM jsonb_array_elements({array}) AS koan_element(value))";
        // PostgreSQL treats NULL as larger than every value, which is the opposite of the framework's sorter,
        // so the placement is stated rather than defaulted.
        return descending ? $"{aggregate} DESC NULLS LAST" : $"{aggregate} ASC NULLS FIRST";
    }

    public string QuoteIdent(string ident) => Quote(ident);
    public string EscapeLike(string fragment) => fragment.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
    public string Parameter(int index) => $"@p{index}";
    public string JsonArrayContains(string columnSql, string parameter) => JsonArrayContains(columnSql, parameter, typeof(string));

    /// <summary>
    /// Each element is read as a scalar read out of a document is — cast to its type (a real number to double precision),
    /// a DateTime rewritten to wall-clock text — and compared with a comparand encoded to match (DATA-0139). A document may hold null rather than an array,
    /// and jsonb_array_elements_text refuses a scalar, so the array-ness is checked.
    /// </summary>
    public string JsonArrayContains(string columnSql, string parameter, Type elementType) =>
        $"EXISTS (SELECT 1 FROM {Elements(columnSql)} item WHERE {Element(elementType)} = {parameter})";

    public object? JsonArrayComparand(object? value, Type elementType) => RelationalDocumentText.Comparand(value);

    private static string Elements(string columnSql) =>
        $"jsonb_array_elements_text(CASE WHEN jsonb_typeof({columnSql}) = 'array' THEN {columnSql} ELSE '[]'::jsonb END)";

    // A real number reads as double precision, which compares with the double it was written from exactly; numeric would
    // meet the comparand at fifteen digits.
    private string Element(Type elementType) =>
        (Nullable.GetUnderlyingType(elementType) ?? elementType) is var type && (type == typeof(double) || type == typeof(float))
            ? "(item)::double precision"
            : DocumentOperand(Cast("item", elementType), elementType);
    public string JsonArrayLength(string columnSql) => $"jsonb_array_length({columnSql})";

    /// <summary>
    /// A set whose members share one type PostgreSQL binds as an array is bound as that one array and compared with
    /// <c>= ANY</c> (or <c>&lt;&gt; ALL</c>), so its size meets no parameter limit (DATA-0162). Each element binds as
    /// the same type it would as its own parameter, so the comparison is the one an IN list makes.
    /// </summary>
    public string? MemberOf(string columnSql, IReadOnlyList<object?> members, bool negate, Func<object?, string> bind)
        => TypedArray(members) is { } array
            ? negate
                ? $"{columnSql} <> ALL({bind(new SqlArrayParameter(array))})"
                : $"{columnSql} = ANY({bind(new SqlArrayParameter(array))})"
            : null;

    /// <summary>PostgreSQL accepts 65,535 parameters in a statement.</summary>
    public int MaximumParameters => 65_535;

    /// <summary>A JSON array column against a set bound as one typed array, each element read and compared as
    /// <see cref="JsonArrayContains(string, string, Type)"/> compares one (DATA-0162).</summary>
    public string? CollectionMemberOf(string columnSql, IReadOnlyList<object?> members, SetMatch match, Type elementType,
        Func<object?, string> bind)
    {
        if (TypedArray(members) is not { } array) return null;
        var set = bind(new SqlArrayParameter(array));
        var element = Element(elementType);
        var any = $"EXISTS (SELECT 1 FROM {Elements(columnSql)} item WHERE {element} = ANY({set}))";
        return match switch
        {
            SetMatch.Any => any,
            SetMatch.None => $"(NOT {any})",
            _ => $"(NOT EXISTS (SELECT 1 FROM unnest({set}) AS koan_set(v) " +
                 $"WHERE koan_set.v NOT IN (SELECT {element} FROM {Elements(columnSql)} item WHERE item IS NOT NULL)))"
        };
    }

    /// <summary>
    /// The members as one typed array when all are non-null and share a type Npgsql binds as an array element the same
    /// way it binds the member alone: text, an integer, a uuid, a numeric, a double or real, a boolean, an interval, a
    /// date, a time, or a timestamp (every member of one <see cref="DateTimeKind"/>, which decides timestamp against
    /// timestamptz). Else null.
    /// </summary>
    internal static Array? TypedArray(IReadOnlyList<object?> members) => members.Count == 0 ? null : members[0] switch
    {
        string => Typed<string>(members),
        long => Typed<long>(members),
        int => Typed<int>(members),
        short => Typed<short>(members),
        Guid => Typed<Guid>(members),
        decimal => Typed<decimal>(members),
        double => Typed<double>(members),
        float => Typed<float>(members),
        bool => Typed<bool>(members),
        TimeSpan => Typed<TimeSpan>(members),
        DateOnly => Typed<DateOnly>(members),
        TimeOnly => Typed<TimeOnly>(members),
        DateTime first => Typed<DateTime>(members) is { } instants && instants.All(instant => instant.Kind == first.Kind)
            ? instants
            : null,
        _ => null
    };

    private static T[]? Typed<T>(IReadOnlyList<object?> members)
    {
        var values = new T[members.Count];
        for (var index = 0; index < members.Count; index++)
        {
            if (members[index] is not T value) return null;
            values[index] = value;
        }
        return values;
    }
    public string JsonArrayElementLike(string columnSql, string patternParameter, string literalParameter) =>
        // Postgres LIKE is case-sensitive regardless of locale, so the pattern rides as-is. jsonb
        // null is a scalar and jsonb_array_elements_text refuses it, so the array-ness is checked —
        // the same guard JsonArrayOrderTerm applies.
        $"EXISTS (SELECT 1 FROM jsonb_array_elements_text(CASE WHEN jsonb_typeof({columnSql}) = 'array' THEN {columnSql} ELSE '[]'::jsonb END) item WHERE item LIKE {patternParameter} ESCAPE '\\')";

    public static string Quote(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
    public static string EscapePath(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string Cast(string expression, Type type)
    {
        var effective = Nullable.GetUnderlyingType(type) ?? type;
        if (effective.IsEnum) effective = typeof(string);
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
