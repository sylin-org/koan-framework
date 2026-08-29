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
}
