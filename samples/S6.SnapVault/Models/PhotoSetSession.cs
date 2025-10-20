using Koan.Data.Core.Model;

namespace S6.SnapVault.Models;

/// <summary>
/// PhotoSet Session - Volatile browsing context
/// Stores photo ID snapshot for consistent navigation
/// Auto-expires, no cleanup needed
/// </summary>
public class PhotoSetSession : Entity<PhotoSetSession>
{
    /// <summary>
    /// Context type: all-photos, search, collection, favorites
    /// </summary>
    public string Context { get; set; } = "all-photos";

    /// <summary>
    /// Search query (for context=search)
    /// </summary>
    public string? SearchQuery { get; set; }

    /// <summary>
    /// Search alpha: 0.0 = exact, 1.0 = semantic (for context=search)
    /// </summary>
    public double? SearchAlpha { get; set; }

    /// <summary>
    /// Collection ID (for context=collection)
    /// </summary>
    public string? CollectionId { get; set; }

    /// <summary>
    /// Sort field: capturedAt, createdAt, rating, fileName
    /// </summary>
    public string SortBy { get; set; } = "capturedAt";

    /// <summary>
    /// Sort order: asc, desc
    /// </summary>
    public string SortOrder { get; set; } = "desc";

    /// <summary>
    /// Snapshot of photo IDs in sorted order
    /// Consistent results during navigation
    /// </summary>
    public List<string> PhotoIds { get; set; } = new();

    /// <summary>
    /// Total count (cached)
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// When created (for TTL/cleanup)
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
