using Koan.Data.Abstractions.Filtering;

namespace Koan.Data.Relational.Linq;

/// <summary>Defines the minimal SQL grammar required by Koan's relational filter translator.</summary>
public interface ILinqSqlDialect
{
    string QuoteIdent(string ident);
    string EscapeLike(string fragment);
    string Parameter(int index);
    string JsonArrayContains(string columnSql, string parameter);
    string JsonArrayLength(string columnSql);

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
