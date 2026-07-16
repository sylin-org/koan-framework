namespace Koan.Cache;

/// <summary>A fixed-size outcome for one scalar, finite, or asynchronous Entity cache-entry eviction.</summary>
/// <remarks>
/// The outcome reports completed cache-writer calls. A completed removal also requests peer invalidation, even when
/// the selected local/remote topology did not contain the entry. Store removal and peer carriage are not atomic.
/// </remarks>
public sealed class EntityCacheEviction
{
    internal EntityCacheEviction(
        Type entityType,
        long enumerated,
        long removed,
        long absent,
        long skipped,
        long failed,
        bool sourceCompleted)
    {
        EntityType = entityType;
        Enumerated = enumerated;
        Removed = removed;
        Absent = absent;
        Skipped = skipped;
        Failed = failed;
        SourceCompleted = sourceCompleted;
    }

    /// <summary>The Entity type whose cache entries were addressed.</summary>
    public Type EntityType { get; }

    /// <summary>Source items whose eviction processing began.</summary>
    public long Enumerated { get; }

    /// <summary>Entries reported present and removed by the selected cache topology.</summary>
    public long Removed { get; }

    /// <summary>
    /// Removal calls that completed without finding an entry in the selected topology. Peer invalidation was still
    /// requested, so this is a successful idempotent outcome rather than a failure.
    /// </summary>
    public long Absent { get; }

    /// <summary>Entities skipped because their identifier was unset and therefore could never have been cached.</summary>
    public long Skipped { get; }

    /// <summary>Entities whose removal did not reach a confirmed outcome.</summary>
    public long Failed { get; }

    /// <summary>The confirmed prefix of completed removal calls.</summary>
    public long Confirmed => Removed + Absent;

    /// <summary>True when the source reached its natural end.</summary>
    public bool SourceCompleted { get; }
}
