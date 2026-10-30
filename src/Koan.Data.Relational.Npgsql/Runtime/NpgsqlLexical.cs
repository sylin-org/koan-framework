using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Lexical;
using Koan.Data.Relational.Orchestration;

namespace Koan.Data.Relational.Npgsql.Runtime;

/// <summary>
/// How PostgreSQL spells an entity's lexical index: one stored generated <c>tsvector</c> column the server computes
/// from the declared values on every write, and a GIN index over it. The column rides the row's own statement, so an
/// insert, an update and a delete keep the index in step inside whatever transaction the row is written in, and a
/// table that gains the column is computed for every row it already holds.
/// </summary>
internal static class NpgsqlLexical
{
    /// <summary>The generated column, named like a managed field so it is never mistaken for application data.</summary>
    internal const string ColumnName = "__koan_lexical";

    /// <summary>The prefix of the index name; the table name follows, so the name is unique per table and per partition.</summary>
    private const string IndexPrefix = "LX_";

    internal static string IndexName(RelationalTableDefinition table) => NpgsqlIndexNames.Fit(IndexPrefix + table.Name);

    /// <summary>The declared values as one text, each read the way a filter reads it and an absent value as nothing.</summary>
    internal static string Text(NpgsqlDialect dialect, RelationalLexicalDefinition lexical) =>
        string.Join(" || ' ' || ", lexical.Parts.Select(part =>
            $"coalesce({dialect.Read(part.Path, MappingValueShape.Scalar, part.PhysicalType)}, '')"));

    /// <summary>The configuration literal; the language is a validated identifier, so it is spelled as one.</summary>
    internal static string Configuration(RelationalLexicalDefinition lexical) => $"'{lexical.Language}'::regconfig";

    internal static string Vector(NpgsqlDialect dialect, RelationalLexicalDefinition lexical) =>
        $"to_tsvector({Configuration(lexical)}, {Text(dialect, lexical)})";

    /// <summary>
    /// What the column was built from, recorded as its comment so a later declaration (a field added, a language
    /// changed) is noticed without parsing the server's own rendering of the expression back.
    /// </summary>
    internal static string Stamp(NpgsqlDialect dialect, RelationalLexicalDefinition lexical) =>
        $"koan.lexical|{Vector(dialect, lexical)}";

    /// <summary>
    /// The search text: every term joined by OR, so a row holding any term is a match and a row holding more of them
    /// ranks higher. <c>websearch_to_tsquery</c> takes it as words and never refuses it. It has no prefix, so a query with
    /// a word still being typed goes to <c>to_tsquery</c> instead, spelled in that function's grammar: the terms hold
    /// letters and digits only, so none of them can be read as an operator.
    /// </summary>
    internal static string Match(LexicalQuery query) => query.Typing is null
        ? string.Join(" OR ", query.Terms)
        : string.Join(" | ", query.Terms.Select(term => query.IsTyping(term) ? term + ":*" : term));

    /// <summary>The function that reads <see cref="Match"/> as a <c>tsquery</c>.</summary>
    internal static string QueryFunction(LexicalQuery query) => query.Typing is null ? "websearch_to_tsquery" : "to_tsquery";

    /// <summary>The <c>ts_headline</c> options for a snippet; the markers are quoted so a comma or a quote inside them cannot break the list.</summary>
    internal static string Headline(LexicalSnippet snippet) =>
        $"MaxWords={snippet.Words},MinWords={Math.Max(1, snippet.Words / 2)}," +
        $"StartSel={Quote(snippet.Open)},StopSel={Quote(snippet.Close)}";

    private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
