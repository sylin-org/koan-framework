namespace Koan.Cache;

/// <summary>An Entity cache source or removal failure carrying the fixed-size confirmed prefix.</summary>
public sealed class EntityCacheEvictionException : Exception
{
    /// <summary>Identifies which boundary failed.</summary>
    public enum FailureKind
    {
        /// <summary>The Entity source failed while being enumerated or disposed.</summary>
        SourceFailed,

        /// <summary>An Entity cache key could not be built or its removal did not complete.</summary>
        EvictionFailed
    }

    internal EntityCacheEvictionException(
        FailureKind failure,
        string message,
        EntityCacheEviction eviction,
        Exception innerException)
        : base(message, innerException)
    {
        Failure = failure;
        Eviction = eviction;
    }

    /// <summary>The boundary that failed.</summary>
    public FailureKind Failure { get; }

    /// <summary>The fixed-size confirmed prefix observed before failure.</summary>
    public EntityCacheEviction Eviction { get; }
}
