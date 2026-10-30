using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Lexical;

namespace Koan.Data.Core;

/// <summary>
/// The qualified lexical statics the <c>Entity.Lexical</c> facade routes to (ARCH-0135: short inside the facade,
/// qualified on the flat static). Each resolves the routed repository under the ambient context and asks it through
/// its <see cref="ILexicalSearchRepository{TEntity,TKey}"/> facet, which the facade guards and scopes.
/// </summary>
public static partial class Data<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    /// <summary>The page of ranked matches <paramref name="query"/> asks for, among the rows
    /// <paramref name="scope"/> holds for and within the caller's read scope.</summary>
    public static Task<IReadOnlyList<LexicalMatch<TKey>>> LexicalSearch(LexicalQuery query, Filter? scope = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return RequireLexical().Search(query, scope, ct);
    }

    /// <summary>Indexes every row the container holds and answers how many rows the index now covers.</summary>
    public static Task<long> LexicalRebuild(CancellationToken ct = default) => RequireLexical().Rebuild(ct);

    /// <summary>What the routed store holds of the entity's lexical index now.</summary>
    public static Task<LexicalIndexStatus> LexicalStatus(CancellationToken ct = default) => RequireLexical().Status(ct);

    private static ILexicalSearchRepository<TEntity, TKey> RequireLexical() =>
        Repo as ILexicalSearchRepository<TEntity, TKey>
        ?? throw new NotSupportedException(
            $"The repository routed for {typeof(TEntity).Name} does not expose lexical search; an Entity variant view and a " +
            "repository outside the Data facade cannot be searched lexically.");
}
