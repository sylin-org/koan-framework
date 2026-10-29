namespace Koan.Data.Abstractions.Lexical;

/// <summary>What one store holds of an entity's lexical index, measured at the time of asking.</summary>
/// <param name="Provider">The provider that keeps the index.</param>
/// <param name="Fields">The entity's declared lexical properties, in declaration order.</param>
/// <param name="Language">The language the index tokenizes by.</param>
/// <param name="Exists">Whether the store holds the index structures at all.</param>
/// <param name="Rows">How many rows the entity's container holds.</param>
/// <param name="Indexed">How many of them the index covers.</param>
/// <param name="Current">
/// Whether the index covers every row. It is false for rows that existed before the declaration, until a rebuild
/// indexes them, and for an index the store does not hold.
/// </param>
public sealed record LexicalIndexStatus(
    string Provider,
    IReadOnlyList<string> Fields,
    string Language,
    bool Exists,
    long Rows,
    long Indexed,
    bool Current);
