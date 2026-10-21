using Koan.Data.Abstractions.Filtering;

namespace Koan.Data.Abstractions;

/// <summary>
/// A native batch that can queue a delete guarded by a stored-row predicate. The delete runs inside the batch as one
/// native statement, <c>DELETE ... WHERE identity AND guard</c>, and its outcome is what the store removed. The facade
/// queues a scoped batch delete this way when the adapter advertises
/// <see cref="Capabilities.DataCaps.Write.ConditionalDelete"/> (DATA-0156).
/// </summary>
public interface IConditionalBatchDelete<TEntity, TKey>
{
    /// <summary>Queues a delete of <paramref name="id"/> that removes the row only while it matches <paramref name="guard"/>.</summary>
    IBatchSet<TEntity, TKey> DeleteIf(TKey id, Filter guard);
}
