using System.Collections.Concurrent;
using Koan.Data.Core.Metadata;

namespace Koan.Data.Core.Pipeline;

/// <summary>
/// The ordered set of write-stamps for an entity type (DATA-0105 Write-stamp stage) — the seam onto which the
/// built-in concerns (identity, <c>[Timestamp]</c>) are re-homed and onto which tenancy registers (phase 4). The
/// plan is composed once per type and memoized at the Type plane; <see cref="ApplyAll"/> runs on Upsert/UpsertMany
/// and <see cref="ApplyBatch"/> runs the batch subset. Order is identity first, then the remaining stamps.
/// </summary>
internal sealed class StorageWritePlan
{
    private static readonly ConcurrentDictionary<Type, StorageWritePlan> Cache = new();

    private readonly IWriteStamp[] _full;
    private readonly IWriteStamp[] _batch;

    private StorageWritePlan(IWriteStamp[] full)
    {
        _full = full;
        _batch = full.Where(s => s.AppliesInBatch).ToArray();
    }

    public static StorageWritePlan For(Type entityType) => Cache.GetOrAdd(entityType, static t => Build(t));

    private static StorageWritePlan Build(Type entityType)
    {
        // Built-in write-stamps, in apply order. Identity is invariant and always present; the timestamp stamp is
        // added only when the entity actually has [Timestamp] properties (so a type without them keeps an
        // identity-only plan — the common, near-inline fast path).
        var stamps = new List<IWriteStamp> { new IdentityWriteStamp(entityType) };

        var bag = new TimestampPropertyBag(entityType);
        if (bag.HasTimestamp) stamps.Add(new TimestampWriteStamp(bag));

        return new StorageWritePlan(stamps.ToArray());
    }

    /// <summary>Apply every write-stamp (Upsert / UpsertMany).</summary>
    public void ApplyAll(object entity)
    {
        foreach (var stamp in _full) stamp.Apply(entity);
    }

    /// <summary>Apply only the batch-eligible write-stamps (the batch path; timestamps are excluded).</summary>
    public void ApplyBatch(object entity)
    {
        foreach (var stamp in _batch) stamp.Apply(entity);
    }
}
