using Koan.Data.Abstractions;

namespace Koan.Data.Core;

public sealed class AggregateIdentityManager : IAggregateIdentityManager
{
    public ValueTask EnsureIdAsync<TEntity, TKey>(TEntity entity, CancellationToken ct = default)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        // Shared with the write-stamp pipeline (Pipeline.IdentityWriteStamp) so the rule cannot drift.
        AggregateIdentity.Ensure(entity, AggregateMetadata.GetIdSpec(typeof(TEntity)));
        return ValueTask.CompletedTask;
    }
}
