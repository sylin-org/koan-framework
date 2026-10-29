using System.Reflection;

namespace Koan.Data.Core;

/// <summary>
/// An entity's lexical declaration, compiled once per type: the string properties its <c>[Lexical]</c> attributes name,
/// in declaration order, and the one language they tokenize by. It stands beside <see cref="IndexSpec"/> rather than
/// inside it because a lexical index is not a key: it has no uniqueness, no order, no expression a planner matches, and
/// a store serves it from a structure of its own (an FTS5 table, a <c>tsvector</c> column) rather than from a B-tree.
/// </summary>
public sealed record LexicalSpec(IReadOnlyList<PropertyInfo> Properties, string Language)
{
    /// <summary>The language an entity takes when no declared property names one.</summary>
    public const string DefaultLanguage = "english";

    /// <summary>The declared property names, in declaration order.</summary>
    public IReadOnlyList<string> Fields { get; } = Properties.Select(static property => property.Name).ToArray();
}
