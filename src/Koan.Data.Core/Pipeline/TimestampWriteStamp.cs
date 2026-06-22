using Koan.Data.Core.Metadata;

namespace Koan.Data.Core.Pipeline;

/// <summary>
/// Built-in write-stamp that applies <c>[Timestamp]</c> set-once (CreatedAt) and on-save (UpdatedAt) semantics,
/// reusing the compiled-delegate <see cref="TimestampPropertyBag"/>. NOT applied on the batch path — the shipped
/// invariant that batch writes are not timestamp-stamped.
/// </summary>
internal sealed class TimestampWriteStamp : IWriteStamp
{
    private readonly TimestampPropertyBag _bag;

    public TimestampWriteStamp(TimestampPropertyBag bag) => _bag = bag;

    public bool AppliesInBatch => false;

    /// <summary>100 — after identity, before any value-protecting transform.</summary>
    public int Priority => 100;

    public void Apply(object entity) => _bag.UpdateTimestamp(entity);
}
