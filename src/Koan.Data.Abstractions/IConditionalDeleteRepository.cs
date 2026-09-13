using Koan.Data.Abstractions.Filtering;

namespace Koan.Data.Abstractions;

/// <summary>
/// Atomically deletes one immutable identity only while its stored row matches a row-only guard.
/// Adapters advertise this through <see cref="Capabilities.DataCaps.Write.ConditionalDelete"/>;
/// a read followed by ordinary delete is not an equivalent fallback.
/// </summary>
public interface IConditionalDeleteRepository<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    Task<bool> ConditionalDeleteAsync(TKey id, Filter guard, CancellationToken ct = default);
}
