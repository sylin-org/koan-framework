using Koan.Data.Abstractions.Filtering;

namespace Koan.Data.Abstractions;

/// <summary>
/// Atomically deletes one immutable identity only while its stored row matches a row-only guard.
/// Adapters advertise this through <see cref="Capabilities.DataCaps.Write.ConditionalDelete"/>;
/// a read followed by ordinary delete is not an equivalent fallback.
/// </summary>
/// <remarks>
/// The guard is evaluated against the stored row, including the framework-managed values stamped on it (a tenant or
/// scope discriminator, a soft-delete marker). The facade passes the caller's read scope here, so a scoped delete runs
/// as one native statement, <c>DELETE ... WHERE identity AND scope</c> (DATA-0156).
/// </remarks>
public interface IConditionalDeleteRepository<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    Task<bool> ConditionalDeleteAsync(TKey id, Filter guard, CancellationToken ct = default);

    /// <summary>
    /// Deletes each of <paramref name="ids"/> whose stored row matches <paramref name="guard"/> and returns the number
    /// of rows the store removed. Each row is removed atomically with its guard. The default deletes one identity at a
    /// time; an adapter overrides it with one native statement.
    /// </summary>
    async Task<int> ConditionalDeleteMany(IReadOnlyList<TKey> ids, Filter guard, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(guard);
        var deleted = 0;
        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            if (await ConditionalDeleteAsync(id, guard, ct).ConfigureAwait(false)) deleted++;
        }
        return deleted;
    }
}
