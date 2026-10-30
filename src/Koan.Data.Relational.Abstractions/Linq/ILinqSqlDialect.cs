using Koan.Data.Abstractions.Filtering;

namespace Koan.Data.Relational.Linq;

/// <summary>Defines the minimal SQL grammar required by Koan's relational filter translator.</summary>
public interface ILinqSqlDialect
{
    string QuoteIdent(string ident);
    string EscapeLike(string fragment);
    string Parameter(int index);
    string JsonArrayContains(string columnSql, string parameter);

    /// <summary>
    /// SQL answering "some element of the JSON array in <paramref name="columnSql"/> equals <paramref name="parameter"/>",
    /// each element read as an element of <paramref name="elementType"/> the way a scalar read out of a document is
    /// (DATA-0139), against a comparand from <see cref="JsonArrayComparand"/>. A column holding no array matches nothing.
    /// The default ignores the type.
    /// </summary>
    string JsonArrayContains(string columnSql, string parameter, Type elementType) => JsonArrayContains(columnSql, parameter);

    /// <summary>
    /// The value an element of <paramref name="elementType"/> is compared with, from the comparand already encoded to the
    /// canonical store form (DATA-0100): the same text or number <see cref="JsonArrayContains(string, string, Type)"/>
    /// reads the element as. Both <c>Has</c> and the list operators bind it. The default binds it unchanged.
    /// </summary>
    object? JsonArrayComparand(object? value, Type elementType) => value;

    string JsonArrayLength(string columnSql);

    /// <summary>
    /// SQL answering "<paramref name="columnSql"/> is one of <paramref name="members"/>" (or, with
    /// <paramref name="negate"/>, "is none of them"), unparenthesized, with the whole set bound as one value through
    /// <paramref name="bind"/>, so a set of any size meets no parameter limit — or <see langword="null"/> when this store
    /// cannot carry these members in one value. Each member must compare exactly as it would bound alone, so the answer is
    /// Eq's answer member by member. The members are non-null and already encoded to the form the store compares; a null
    /// value of the column is the caller's to handle. The default declines, and the translator binds one parameter per
    /// member in IN lists of at most <see cref="MaximumInListMembers"/> (DATA-0162).
    /// </summary>
    string? MemberOf(string columnSql, IReadOnlyList<object?> members, bool negate, Func<object?, string> bind) => null;

    /// <summary>
    /// SQL answering how the JSON array in <paramref name="columnSql"/> meets <paramref name="members"/>
    /// (<paramref name="match"/>: some element is a member, every member is an element, or no element is one), with the
    /// whole set bound as one value through <paramref name="bind"/> and each element read as an element of
    /// <paramref name="elementType"/> and compared exactly as <see cref="JsonArrayContains(string, string, Type)"/> compares
    /// one — or <see langword="null"/> when this store cannot carry these members in one value, and the translator binds one
    /// <see cref="JsonArrayContains(string, string, Type)"/> per member (DATA-0162). The members come from
    /// <see cref="JsonArrayComparand"/>.
    /// </summary>
    string? CollectionMemberOf(string columnSql, IReadOnlyList<object?> members, SetMatch match, Type elementType,
        Func<object?, string> bind) => null;

    /// <summary>
    /// Why <see cref="MemberOf"/> or <see cref="CollectionMemberOf"/> declined to bind <paramref name="members"/> as one
    /// value, for the refusal of a statement that is still over <see cref="MaximumParameters"/> — a missing table type
    /// and the DDL that creates it, say. Null when there is nothing to add to the member types.
    /// </summary>
    string? OneValueRefusal(IReadOnlyList<object?> members) => null;

    /// <summary>
    /// The most members one IN list holds in this dialect. A longer set of per-member parameters is split into several
    /// IN lists, joined by OR (or by AND for NOT IN). The default sets no bound.
    /// </summary>
    int MaximumInListMembers => int.MaxValue;

    /// <summary>
    /// Whether a set binds one parameter per member while the statement fits within <see cref="MaximumParameters"/>, and
    /// moves to <see cref="MemberOf"/> only when it does not: true where the store plans a short IN list better than a
    /// bound set. The default (false) binds every set through <see cref="MemberOf"/> when the dialect carries it.
    /// </summary>
    bool PrefersSetParameters => false;

    /// <summary>
    /// The most parameters a statement may bind on this store, as measured. A filter is translated against it less the
    /// parameters its own statement adds besides the filter (a conditional replace's SET values and identity), which the
    /// caller passes to <c>Translate</c>. While a statement is over it, the translator binds its largest remaining per-member
    /// list — an <c>In</c>, <c>Nin</c>, <c>HasAny</c>, <c>HasAll</c> or <c>HasNone</c> — as one value through
    /// <see cref="MemberOf"/> or <see cref="CollectionMemberOf"/>; it refuses only when nothing more can move and the
    /// statement is still over, naming what could not, rather than sending a statement the store would reject
    /// (DATA-0162). The default sets no bound.
    /// </summary>
    int MaximumParameters => int.MaxValue;

    /// <summary>
    /// SQL answering "some element of the JSON array in <paramref name="columnSql"/> contains the
    /// substring". Two bound values arrive: <paramref name="patternParameter"/> carries the
    /// <see cref="EscapeLike"/>-escaped, wildcard-extended LIKE pattern (<c>%value%</c>), and
    /// <paramref name="literalParameter"/> carries the raw substring. Dialects whose LIKE is
    /// case-sensitive by contract use the pattern (with whatever ESCAPE clause their escape literal
    /// requires); dialects whose LIKE folds case under default collations must match on the raw
    /// literal (or force a binary collation) so a pushed filter answers exactly what the
    /// case-sensitive floor answers. Null, missing, and empty arrays must answer false.
    /// </summary>
    string JsonArrayElementLike(string columnSql, string patternParameter, string literalParameter);

    /// <summary>
    /// SQL answering a case-sensitive scalar string match: <paramref name="op"/> is
    /// <see cref="FilterOperator.StartsWith"/>, <see cref="FilterOperator.EndsWith"/> or
    /// <see cref="FilterOperator.Contains"/>, and the answer must be the oracle's ordinal comparison, false for a null
    /// value. <paramref name="bind"/> binds one value and returns its placeholder, so a dialect binds only what it
    /// uses. The default is <c>LIKE</c> over the <see cref="EscapeLike"/>-escaped pattern, which is right where LIKE is
    /// case-sensitive under the store's default collation; a dialect whose LIKE folds case must match on the bound
    /// literal instead, as <see cref="JsonArrayElementLike"/> already does.
    /// </summary>
    string StringMatch(string columnSql, FilterOperator op, string value, Func<object?, string> bind)
    {
        var escaped = EscapeLike(value);
        var pattern = op switch
        {
            FilterOperator.StartsWith => escaped + "%",
            FilterOperator.EndsWith => "%" + escaped,
            FilterOperator.Contains => "%" + escaped + "%",
            _ => throw new NotSupportedException($"Operator '{op}' is not a scalar string match.")
        };
        return $"({columnSql} LIKE {bind(pattern)} ESCAPE '\\')";
    }
}
