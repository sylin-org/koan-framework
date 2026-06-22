using System.Collections.Concurrent;
using Koan.Data.Core.Metadata;

namespace Koan.Data.Core.Pipeline;

/// <summary>
/// The ordered set of write-stamps for an entity type (DATA-0105 Write-stamp stage) — the seam onto which the
/// built-in concerns (identity, <c>[Timestamp]</c>) are re-homed and onto which external contributors register via
/// <see cref="StorageWriteContributorRegistry"/> (the classification field-transform, ARCH-0098 §0). The plan is
/// composed once per type and memoized at the Type plane; <see cref="ApplyAll"/> runs on Upsert/UpsertMany and
/// <see cref="ApplyBatch"/> runs the batch subset. Apply order is the stable sort by <see cref="IWriteStamp.Priority"/>
/// (identity 0 → <c>[Timestamp]</c> 100 → contributors), ties preserving insertion order.
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

    /// <summary>Drop the per-type plan memo. Called when a contributor registration changes (boot-only ⇒ rare).</summary>
    internal static void InvalidateCache() => Cache.Clear();

    private static StorageWritePlan Build(Type entityType)
    {
        // Built-in write-stamps. Identity is invariant and always present; the timestamp stamp is added only when
        // the entity actually has [Timestamp] properties (so a type without them keeps an identity-only plan — the
        // common, near-inline fast path).
        var stamps = new List<IWriteStamp> { new IdentityWriteStamp(entityType) };

        var bag = new TimestampPropertyBag(entityType);
        if (bag.HasTimestamp) stamps.Add(new TimestampWriteStamp(bag));

        // External contributors (the "open slot", DATA-0105 §0). Off-gated: when none registered this is skipped
        // entirely and the plan is byte-identical to the built-in-only path. A contributor returns null for a type
        // it does not apply to (e.g. no classified fields), so only relevant types pay.
        if (!StorageWriteContributorRegistry.IsEmpty)
        {
            foreach (var contributor in StorageWriteContributorRegistry.All)
            {
                var stamp = contributor.Build(entityType);
                if (stamp is not null) stamps.Add(stamp);
            }
        }

        // Stable sort by explicit priority — the DATA-0105 §3 total, stable, explicit order. OrderBy is stable, so
        // equal-priority stamps keep insertion order. With only built-ins this yields [identity, timestamp] exactly
        // as before.
        var ordered = stamps.OrderBy(s => s.Priority).ToArray();
        return new StorageWritePlan(ordered);
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
