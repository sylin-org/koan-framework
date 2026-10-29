using Koan.Data.Abstractions.Filtering;

namespace Koan.Data.Abstractions.Lexical;

/// <summary>
/// An adapter facet: the repository keeps a lexical index over the entity's <c>[Lexical]</c> string properties and
/// answers ranked term searches over it. The repository that implements it also declares
/// <see cref="Capabilities.DataCaps.Query.Lexical"/>. The index is maintained by the store inside the same write unit
/// as the row — a save or remove that fails leaves it as it was, and a write rolled back with its transaction takes
/// its index entry with it.
/// </summary>
public interface ILexicalSearchRepository<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    /// <summary>
    /// The page of matches <paramref name="query"/> asks for, best first, ties broken by identity so two pages over one
    /// result set neither overlap nor skip. <paramref name="scope"/> is the caller's read scope (the tenant, the
    /// not-yet-deleted predicate) and every match satisfies it.
    /// </summary>
    Task<IReadOnlyList<LexicalMatch<TKey>>> Search(LexicalQuery query, Filter? scope, CancellationToken ct = default);

    /// <summary>
    /// Indexes every row the container holds, including rows that existed before the declaration, and answers how
    /// many rows the index now covers.
    /// </summary>
    Task<long> Rebuild(CancellationToken ct = default);

    /// <summary>What the store holds of the index now.</summary>
    Task<LexicalIndexStatus> Status(CancellationToken ct = default);
}
