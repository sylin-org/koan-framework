namespace Koan.Data.Relational.Linq;

/// <summary>
/// Minimal SQL dialect hooks required by the LINQ translator.
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
}
