namespace Koan.Data.Core.Pipeline;

/// <summary>
/// Built-in write-stamp that ensures the entity's identifier (GUID v7 / sortable string id) when it is still at
/// its default. Invariant — never pluggable away. Stamps both the single/many and the batch write paths.
/// </summary>
internal sealed class IdentityWriteStamp : IWriteStamp
{
    private readonly AggregateMetadata.IdSpec? _spec;

    public IdentityWriteStamp(Type entityType) => _spec = AggregateMetadata.GetIdSpec(entityType);

    public bool AppliesInBatch => true;

    public void Apply(object entity) => AggregateIdentity.Ensure(entity, _spec);
}
