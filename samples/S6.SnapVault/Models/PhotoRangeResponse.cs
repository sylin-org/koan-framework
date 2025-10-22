namespace S6.SnapVault.Models;

/// <summary>
/// Response model for range queries
/// Returns a contiguous range of photo metadata
/// </summary>
public class PhotoRangeResponse
{
    /// <summary>
    /// Array of photo metadata objects
    /// </summary>
    public List<PhotoMetadata> Photos { get; set; } = new();

    /// <summary>
    /// Starting index of this range (0-based)
    /// </summary>
    public int StartIndex { get; set; }

    /// <summary>
    /// Number of photos returned
    /// </summary>
    public int Count { get; set; }

    /// <summary>
    /// Total photos in the complete set
    /// </summary>
    public int TotalCount { get; set; }
}

/// <summary>
/// Lightweight photo metadata for efficient caching
/// </summary>
public class PhotoMetadata
{
    /// <summary>
    /// Photo ID
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// Original file name
    /// </summary>
    public string FileName { get; set; } = "";

    /// <summary>
    /// When the photo was captured (EXIF data)
    /// </summary>
    public DateTime? CapturedAt { get; set; }

    /// <summary>
    /// When the photo was uploaded
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// URL to thumbnail image
    /// </summary>
    public string ThumbnailUrl { get; set; } = "";

    /// <summary>
    /// Masonry thumbnail media ID (for grid display)
    /// </summary>
    public string? MasonryThumbnailMediaId { get; set; }

    /// <summary>
    /// Star rating (0-5)
    /// </summary>
    public int Rating { get; set; }

    /// <summary>
    /// Whether photo is favorited
    /// </summary>
    public bool IsFavorite { get; set; }

    /// <summary>
    /// Width in pixels
    /// </summary>
    public int Width { get; set; }

    /// <summary>
    /// Height in pixels
    /// </summary>
    public int Height { get; set; }
}
