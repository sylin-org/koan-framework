using Koan.Media.Abstractions.Model;
using Koan.Storage.Infrastructure;
using Koan.Data.Vector.Abstractions;

namespace S6.SnapVault.Models;

/// <summary>
/// Full-resolution photo asset with complete metadata (stored in cold tier for cost optimization)
/// </summary>
[StorageBinding(Profile = "cold", Container = "photos")]
public class PhotoAsset : MediaEntity<PhotoAsset>
{
    // Event relationship
    public string EventId { get; set; } = "";
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CapturedAt { get; set; } // From EXIF

    // Derivative relationships (using framework's media graph)
    // ThumbnailMediaId already provided by MediaEntity<T> (square, for grid views)
    public string? MasonryThumbnailMediaId { get; set; } // Aspect-ratio preserving, for masonry views
    public string? GalleryMediaId { get; set; }

    // Original file metadata
    public string OriginalFileName { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }

    // EXIF metadata
    public string? CameraModel { get; set; }
    public string? LensModel { get; set; }
    public string? FocalLength { get; set; }
    public string? Aperture { get; set; }
    public string? ShutterSpeed { get; set; }
    public int? ISO { get; set; }
    public GpsCoordinates? Location { get; set; }

    // AI-generated metadata
    public List<string> DetectedObjects { get; set; } = new();
    public string MoodDescription { get; set; } = "";
    public List<string> AutoTags { get; set; } = new();

    // Vector for semantic search (no attribute needed - framework detects float[] automatically)
    public float[]? Embedding { get; set; }

    // User interactions
    public int ViewCount { get; set; }
    public bool IsFavorite { get; set; }
    public int Rating { get; set; } // 0-5 stars

    // Processing
    public ProcessingStatus ProcessingStatus { get; set; } = ProcessingStatus.Pending;
}

public class GpsCoordinates
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double? Altitude { get; set; }
}
