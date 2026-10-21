using System.Globalization;

namespace Koan.Data.Relational.Mapping;

/// <summary>
/// The comparable text of a Guid or a DateTime held inside a JSON document. DATA-0100 governs neither type, so a
/// document holds them in the serializer's own spelling: a Guid as lower-case <c>D</c> text, a DateTime as round-trip
/// ISO text whose fraction is trimmed and whose zone suffix depends on its Kind (<c>Z</c>, an offset, or nothing).
/// Neither is the form a driver binds the CLR value in, so a dialect that compares document text uses these, on both
/// sides, to answer what the oracle answers (DATA-0139).
/// </summary>
public static class RelationalDocumentText
{
    private const string WallClock = "yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'fffffff";

    /// <summary>
    /// A comparand in the document's comparable text: a Guid as its <c>D</c> text, a DateTime as fixed-width wall-clock
    /// text to the tick. The oracle compares DateTime ticks and ignores Kind, so the zone is not part of the value.
    /// </summary>
    public static object? Comparand(object? value) => value switch
    {
        Guid guid => guid.ToString("D", CultureInfo.InvariantCulture),
        DateTime dateTime => dateTime.ToString(WallClock, CultureInfo.InvariantCulture),
        _ => value
    };

    /// <summary>
    /// Rewrites document text holding a DateTime (<paramref name="text"/>) into the fixed-width wall-clock form of
    /// <see cref="Comparand"/>: the first nineteen characters, then the fraction padded to seven digits, dropping a
    /// <c>Z</c> or <c>±hh:mm</c> suffix. Portable across SQLite, PostgreSQL and DuckDB (<c>substr</c>, <c>length</c>,
    /// <c>||</c>, <c>CASE</c>); NULL stays NULL.
    /// </summary>
    public static string DateTimeOperand(string text)
    {
        var zone = $"(CASE WHEN substr({text}, length({text}), 1) = 'Z' THEN 1 " +
                   $"WHEN substr({text}, length({text}) - 5, 1) IN ('+', '-') THEN 6 ELSE 0 END)";
        var fraction = $"(CASE WHEN substr({text}, 20, 1) = '.' " +
                       $"THEN substr({text}, 21, length({text}) - 20 - {zone}) ELSE '' END)";
        return $"(substr({text}, 1, 19) || '.' || substr({fraction} || '0000000', 1, 7))";
    }

    /// <summary>Whether <paramref name="type"/> (or its nullable form) is held as document text this type rewrites.</summary>
    public static bool IsDateTime(Type type) => (Nullable.GetUnderlyingType(type) ?? type) == typeof(DateTime);
}
