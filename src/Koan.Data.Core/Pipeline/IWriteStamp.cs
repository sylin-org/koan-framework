namespace Koan.Data.Core.Pipeline;

/// <summary>
/// A synchronous write-stamp: mutate an entity in place just before persist (DATA-0105 Write-stamp stage).
/// Framework-owned and built once per entity type from declared metadata — identity convention, <c>[Timestamp]</c>,
/// and (from phase 4) the ambient tenant. Synchronous and allocation-free per the hot-path discipline.
/// </summary>
internal interface IWriteStamp
{
    /// <summary>Apply the stamp to <paramref name="entity"/> in place.</summary>
    void Apply(object entity);

    /// <summary>
    /// Whether this stamp also applies on the batch write path. Identity (and, from phase 4, tenant) must stamp
    /// batch writes; <see cref="TimestampWriteStamp"/> must NOT — the shipped <c>BatchFacade</c> invariant is that
    /// batch writes are not timestamp-stamped today.
    /// </summary>
    bool AppliesInBatch { get; }
}
