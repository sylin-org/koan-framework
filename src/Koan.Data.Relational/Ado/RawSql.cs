namespace Koan.Data.Relational.Ado;

/// <summary>
/// The text rule under a raw provider query (<c>QueryRaw</c> / <c>CountRaw</c>). A raw query is an expert escape hatch
/// for trusted callers, not a sanitizer for request text; this is the floor beneath it, so one call cannot become
/// several statements and a mistake fails with the correction instead of reaching the engine.
/// </summary>
public static class RawSql
{
    /// <summary>
    /// Requires <paramref name="sql"/> to be a single statement by refusing the statement separator <c>;</c> (and NUL)
    /// wherever it appears. A separator inside a literal or a comment is refused too: telling those apart means lexing
    /// the dialect, and a lexer that disagrees with the engine is how a second statement gets through. A value that
    /// contains <c>;</c> travels as a parameter.
    /// </summary>
    /// <exception cref="NotSupportedException">The text could carry more than one statement.</exception>
    public static void RequireSingleStatement(string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);
        if (sql.AsSpan().IndexOfAny(';', '\0') < 0) return;
        throw new NotSupportedException(
            "A raw query runs as exactly one statement, so its text must not contain ';' or NUL. " +
            "Remove the separator and pass literal values as parameters (@name) instead of inlining them.");
    }
}
