namespace Koan.Cache;

/// <summary>A canceled Entity cache eviction carrying the fixed-size confirmed prefix.</summary>
public sealed class EntityCacheEvictionCanceledException : OperationCanceledException
{
    internal EntityCacheEvictionCanceledException(
        string message,
        EntityCacheEviction eviction,
        OperationCanceledException innerException,
        CancellationToken cancellationToken)
        : base(message, innerException, cancellationToken)
        => Eviction = eviction;

    /// <summary>The fixed-size confirmed prefix observed before cancellation.</summary>
    public EntityCacheEviction Eviction { get; }
}
