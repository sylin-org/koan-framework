namespace Koan.Data.Abstractions;

/// <summary>
/// Optional native seam for adapters that can distinguish insert from update at the mutation boundary.
/// Delete outcome remains Data-owned so visibility, lifecycle, and scoped semantics stay uniform.
/// </summary>
public interface IMutationOutcomeRepository<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    Task<MutationResult<TEntity, TKey>> UpsertWithOutcome(
        TEntity model,
        CancellationToken ct = default);
}
