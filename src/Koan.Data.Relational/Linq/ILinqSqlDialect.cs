namespace Koan.Data.Relational.Linq;

/// <summary>
/// Minimal SQL dialect hooks required by the relational filter translator.
/// Keep this intentionally tiny and separate from schema concerns.
/// </summary>
public interface ILinqSqlDialect
{
    /// <summary>Quotes an identifier (table or column name).</summary>
    string QuoteIdent(string ident);

    /// <summary>
    /// Escapes LIKE special characters in a pattern fragment so it can be used safely with ESCAPE '\\'.
    /// Implementations should escape \\ (backslash), % and _.
    /// </summary>
    string EscapeLike(string fragment);

    /// <summary>
    /// Gets a parameter placeholder for the given index, including the prefix (e.g., @p0).
    /// </summary>
    string Parameter(int index);

    /// <summary>
    /// Emits a boolean predicate that is true when the JSON-array column (<paramref name="columnSql"/>)
    /// contains at least one element equal to the value at <paramref name="parameter"/>. Used to lower
    /// collection-containment operators (Has/HasAny/HasAll/HasNone) onto native JSON-array querying.
    /// <para>DATA-XXXX: List&lt;string&gt; is stored as a JSON array inside the entity document/column.</para>
    /// </summary>
    string JsonArrayContains(string columnSql, string parameter);

    /// <summary>
    /// Emits a scalar SQL expression that yields the number of elements in the JSON-array column
    /// (<paramref name="columnSql"/>). Used to lower the <c>Size</c> collection operator.
    /// </summary>
    string JsonArrayLength(string columnSql);
}
