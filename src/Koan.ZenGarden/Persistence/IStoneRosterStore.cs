namespace Koan.ZenGarden.Persistence;

/// <summary>
/// Abstraction for persisting and loading the Moss Stone topology roster.
/// </summary>
internal interface IStoneRosterStore
{
    /// <summary>
    /// Loads all persisted Stones that have not exceeded their TTL.
    /// Returns an empty collection on first run or if the file is missing/corrupt.
    /// </summary>
    Task<IReadOnlyList<CachedMossStone>> LoadAsync(CancellationToken ct = default);

    /// <summary>
    /// Persists the given Stones to disk. Reads existing entries first, merges
    /// (deduplicating by CacheKey, preferring newer LastSeenUtc), then atomically
    /// replaces the file.
    /// </summary>
    Task PersistAsync(IEnumerable<CachedMossStone> stones, CancellationToken ct = default);
}
