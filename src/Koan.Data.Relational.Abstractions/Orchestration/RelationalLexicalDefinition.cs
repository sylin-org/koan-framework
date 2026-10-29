namespace Koan.Data.Relational.Orchestration;

/// <summary>
/// An entity's lexical index resolved onto the physical values a store tokenizes, in the terms the store's dialect
/// speaks. It is not a <see cref="RelationalIndexDefinition"/>: it has no keys, no uniqueness and no expression a
/// planner matches; each store serves it from a structure of its own (an FTS5 table, a <c>tsvector</c> column with a
/// GIN index) that the executor spells and keeps in step with the row.
/// </summary>
/// <param name="Name">The name the store builds the index under, unique per table.</param>
/// <param name="Parts">The indexed text values, in declaration order.</param>
/// <param name="Language">The text search configuration the store tokenizes by; a lower-case identifier.</param>
public sealed record RelationalLexicalDefinition(
    string Name,
    IReadOnlyList<RelationalIndexPart> Parts,
    string Language)
{
    /// <summary>The declared property names, in declaration order: what an FTS column or a status report is named by.</summary>
    public IReadOnlyList<string> Fields =>
        Parts.Select(static part => part.Path.IsNested ? part.Path.Segments[^1] : part.Path.Name).ToArray();
}
