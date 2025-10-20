using Koan.Data.Core.Model;

namespace S6.SnapVault.Models;

/// <summary>
/// PhotoSet Session - Persistent browsing context
/// Enables saved searches, instant resume, and session history
/// Koan auto-generates GUID v7 ID, works with any provider (MongoDB, PostgreSQL, etc.)
/// </summary>
public class PhotoSetSession : Entity<PhotoSetSession>
{
    /// <summary>
    /// User-defined name for the session
    /// Example: "Sunset Beach", "Best of 2024"
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Auto-generated or user-edited description
    /// Example: "Search: 'sunset beach golden hour'"
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Whether this session is pinned for quick access
    /// </summary>
    public bool IsPinned { get; set; }

    /// <summary>
    /// UI accent color (hex code)
    /// Example: "#FF6B35"
    /// </summary>
    public string? Color { get; set; }

    /// <summary>
    /// Custom icon (emoji or icon name)
    /// Example: "🌅", "star", "heart"
    /// </summary>
    public string? Icon { get; set; }

    /// <summary>
    /// Context type: all-photos, search, collection, favorites
    /// </summary>
    public string Context { get; set; } = "all-photos";

    /// <summary>
    /// Search query for semantic search context
    /// </summary>
    public string? SearchQuery { get; set; }

    /// <summary>
    /// Search alpha (0.0 = exact, 1.0 = semantic)
    /// </summary>
    public double? SearchAlpha { get; set; }

    /// <summary>
    /// Collection ID for collection context
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
    /// Provides consistent results even if new photos are added
    /// </summary>
    public List<string> PhotoIds { get; set; } = new();

    /// <summary>
    /// Total count of photos in this session
    /// Cached to avoid recounting
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// When this session was created
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Last time this session was accessed
    /// Used for sorting recent sessions
    /// </summary>
    public DateTimeOffset LastAccessedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Number of times this session has been viewed
    /// </summary>
    public int ViewCount { get; set; }

    /// <summary>
    /// Photo IDs that were opened in lightbox during this session
    /// Useful for analytics and "continue where you left off"
    /// </summary>
    public List<string> PhotosViewed { get; set; } = new();

    /// <summary>
    /// Total time spent in this session
    /// </summary>
    public TimeSpan TotalTimeSpent { get; set; }
}
