using Koan.Data.Abstractions;

namespace Koan.Data.Relational.Mapping;

public interface IRelationalMappingDialect : Linq.ILinqSqlDialect
{
    string Read(PhysicalPath path, MappingValueShape shape, Type physicalType);

    /// <summary>
    /// The expression a filter compares for a scalar read out of a JSON document (<paramref name="readSql"/>, from
    /// <see cref="Read"/>). Types DATA-0100 does not govern, such as Guid and DateTime, are held as the serializer's own
    /// text, which is not the form a driver binds the CLR value in; a dialect can rewrite the operand into a comparable
    /// text here and encode the comparand to match in <see cref="DocumentComparand"/>. Index expressions keep
    /// <see cref="Read"/>. The default leaves the operand alone.
    /// </summary>
    string DocumentOperand(string readSql, Type physicalType) => readSql;

    /// <summary>
    /// The value bound against <see cref="DocumentOperand"/> for a scalar read out of a JSON document, after the
    /// DATA-0100 comparand encoding. The default binds it unchanged.
    /// </summary>
    object? DocumentComparand(object? value, Type physicalType) => value;

    /// <summary>
    /// The complete ORDER BY term for an order key that aggregates over a JSON array inside a document column —
    /// <c>-Sightings.LastChangedAt</c>, meaning "by each widget's latest sighting".
    ///
    /// <para>Every store Koan ships on can express this, and expressing it is what lets the store order and
    /// page the query itself. A dialect that returns <see langword="null"/> is not broken: the framework
    /// finishes the ordering in memory, correctly, at the cost of materializing the whole result. The default
    /// declines so a dialect outside this repository keeps compiling — implement it, do not inherit it.</para>
    /// </summary>
    /// <param name="arraySql">The array itself, already read from the document by <see cref="Read"/>.</param>
    /// <param name="elementSegments">Path to the ordered value within one element.</param>
    /// <param name="max">Take the largest value across the elements, or the smallest.</param>
    /// <param name="descending">Direction of the key, which the dialect appends together with its own null
    /// placement — an element-less array aggregates to NULL, and the framework's sorter puts NULL first
    /// ascending and last descending, which not every store does by default.</param>
    /// <param name="elementValueType">CLR type of that value, for the dialect's own comparison cast —
    /// text where DATA-0100 stores an order-preserving string, numeric where it stores a number.</param>
    string? JsonArrayOrderTerm(
        string arraySql,
        IReadOnlyList<string> elementSegments,
        bool max,
        bool descending,
        Type elementValueType) => null;
}
