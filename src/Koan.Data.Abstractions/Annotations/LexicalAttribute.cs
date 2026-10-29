namespace Koan.Data.Abstractions.Annotations;

/// <summary>
/// Declares a string property as part of the entity's lexical index: the one index a store keeps over the entity's
/// declared text, kept in step with every write, and read by <c>Entity.Lexical.Search(...)</c> as a ranked term search.
///
/// <para>Place it on each string property whose words should be found. The properties of one entity form one index;
/// a search runs over all of them. A store that declares <see cref="Capabilities.DataCaps.Query.Lexical"/> builds and
/// maintains it (SQLite through FTS5, PostgreSQL through a <c>tsvector</c> column); a store that does not refuses the
/// entity's first write, naming the capability and the correction, so the application never runs on a store that
/// cannot search what it declared.</para>
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class LexicalAttribute : Attribute
{
    /// <summary>
    /// The language the store tokenizes and stems by, where it takes one: a PostgreSQL text search configuration name
    /// (<c>english</c>, <c>simple</c>, <c>german</c>), which SQLite maps to its own tokenizer (the Porter stemmer for
    /// <c>english</c>, plain Unicode tokenization otherwise). One language per entity: set it on any one declared
    /// property, or on none to take <c>english</c>. Two properties naming different languages refuse at compile.
    /// </summary>
    public string? Language { get; init; }
}
