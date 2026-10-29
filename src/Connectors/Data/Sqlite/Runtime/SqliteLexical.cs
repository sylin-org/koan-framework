using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Lexical;
using Koan.Data.Relational.Orchestration;

namespace Koan.Data.Connector.Sqlite.Runtime;

/// <summary>
/// How SQLite spells an entity's lexical index: one FTS5 external-content table over a view that reads the declared
/// values out of the row, and three triggers that keep it in step with every insert, update and delete of the row.
///
/// <para>External content keeps one copy of the text — the row's — and FTS5 reads it back through the view for
/// snippets. The triggers run inside the statement that changes the row, so the index entry lands in the same write
/// unit as the row, the ambient atomic commit of DATA-0158 included: a write that fails or rolls back takes its index
/// entry with it. The FTS rowid is the row's rowid, which a text-keyed table keeps for the row's lifetime; the
/// rebuild verb re-derives every entry from the rows.</para>
/// </summary>
internal static class SqliteLexical
{
    private const string Suffix = "__koan_lexical";

    internal static string IndexName(string table) => table + Suffix;
    internal static string SourceName(string table) => table + Suffix + "_source";
    internal static string DocumentSizeName(string table) => table + Suffix + "_docsize";
    private static string InsertTrigger(string table) => table + Suffix + "_ai";
    private static string UpdateTrigger(string table) => table + Suffix + "_au";
    private static string DeleteTrigger(string table) => table + Suffix + "_ad";

    /// <summary>The FTS5 tokenizer for a language: the Porter stemmer is English-only, so every other language tokenizes plainly.</summary>
    internal static string Tokenizer(string language) =>
        string.Equals(language, "english", StringComparison.Ordinal) ? "porter unicode61" : "unicode61";

    /// <summary>The five schema objects the index is made of, each with the statement that creates it, in creation order.</summary>
    internal static IReadOnlyList<(string Name, string Type, string Sql)> Objects(
        SqliteDialect dialect,
        RelationalTableDefinition table,
        RelationalLexicalDefinition lexical)
    {
        var fields = lexical.Fields;
        var columns = string.Join(", ", fields.Select(SqliteDialect.Quote));
        var index = SqliteDialect.Quote(IndexName(table.Name));
        var source = SqliteDialect.Quote(SourceName(table.Name));
        var row = SqliteDialect.Quote(table.Name);
        string Values(string prefix) => string.Join(", ", lexical.Parts.Select(part => Expression(dialect, part, prefix)));
        string Select(string prefix) => string.Join(", ", lexical.Parts.Select((part, position) =>
            $"{Expression(dialect, part, prefix)} AS {SqliteDialect.Quote(fields[position])}"));
        var remove = $"INSERT INTO {index}({index}, rowid, {columns}) VALUES ('delete', old.rowid, {Values("old.")});";
        var add = $"INSERT INTO {index}(rowid, {columns}) VALUES (new.rowid, {Values("new.")});";
        return
        [
            (SourceName(table.Name), "view",
                $"CREATE VIEW {source} AS SELECT rowid AS koan_rowid, {Select(string.Empty)} FROM {row}"),
            (IndexName(table.Name), "table",
                $"CREATE VIRTUAL TABLE {index} USING fts5({columns}, content='{SourceName(table.Name).Replace("'", "''", StringComparison.Ordinal)}', " +
                $"content_rowid='koan_rowid', tokenize='{Tokenizer(lexical.Language)}')"),
            (InsertTrigger(table.Name), "trigger",
                $"CREATE TRIGGER {SqliteDialect.Quote(InsertTrigger(table.Name))} AFTER INSERT ON {row} BEGIN {add} END"),
            (UpdateTrigger(table.Name), "trigger",
                $"CREATE TRIGGER {SqliteDialect.Quote(UpdateTrigger(table.Name))} AFTER UPDATE ON {row} BEGIN {remove} {add} END"),
            (DeleteTrigger(table.Name), "trigger",
                $"CREATE TRIGGER {SqliteDialect.Quote(DeleteTrigger(table.Name))} AFTER DELETE ON {row} BEGIN {remove} END")
        ];
    }

    /// <summary>The statement that removes one of the index's objects, in the form that drops the triggers before the table they target.</summary>
    internal static string Drop(string name, string type) =>
        $"DROP {type.ToUpperInvariant()} IF EXISTS {SqliteDialect.Quote(name)};";

    /// <summary>
    /// The FTS5 query for a search: every term quoted as a phrase and joined by OR, so a row holding any term is a
    /// match and a row holding more of them ranks higher. Quoting keeps FTS5's own grammar (<c>*</c>, <c>:</c>,
    /// <c>NOT</c>) out of reach, and the terms hold letters and digits only.
    /// </summary>
    internal static string Match(LexicalQuery query) =>
        string.Join(" OR ", query.Terms.Select(static term => "\"" + term.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""));

    /// <summary>
    /// The dialect's own read of one indexed value, addressed through <paramref name="prefix"/>: nothing for the view's
    /// row, <c>new.</c> or <c>old.</c> inside a trigger. It is the read filters emit, so the view and the triggers
    /// derive exactly the text a query compares.
    /// </summary>
    private static string Expression(SqliteDialect dialect, RelationalIndexPart part, string prefix) =>
        dialect.Read(part.Path, MappingValueShape.Scalar, part.PhysicalType)
            .Replace("koan_row.", prefix, StringComparison.Ordinal);
}
