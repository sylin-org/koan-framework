using Koan.Media.Abstractions.Model;
using Koan.Storage.Infrastructure;
using Koan.Data.Vector.Abstractions;
using Koan.Data.AI.Attributes;

namespace S6.SnapVault.Models;

/// <summary>
/// Full-resolution photo asset with complete metadata (stored in cold tier for cost optimization)
/// </summary>
[StorageBinding(Profile = "cold", Container = "photos")]
[Embedding(
    Policy = EmbeddingPolicy.Explicit,
    Async = true,
    MaxTokens = 8191,
    Version = 1)]
public class PhotoAsset : MediaEntity<PhotoAsset>
{
    // Event relationship
    public string EventId { get; set; } = "";
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CapturedAt { get; set; } // From EXIF

    // Derivative relationships (using framework's media graph)
    // ThumbnailMediaId already provided by MediaEntity<T> (square, for grid views)
    public string? MasonryThumbnailMediaId { get; set; } // Aspect-ratio preserving, 300px for masonry views
    public string? RetinaThumbnailMediaId { get; set; } // Aspect-ratio preserving, 600px for retina/4K displays
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
    public string DetailedDescription { get; set; } = ""; // AI vision-generated detailed analysis (legacy)
    public AiAnalysis? AiAnalysis { get; set; } // Structured AI analysis (tags, summary, facts)

    // Smart mode classification cache (avoids repeated classification API calls)
    public string? InferredStyleId { get; set; } // FK to AnalysisStyle (detected style)
    public DateTime? InferredAt { get; set; } // When classification was performed

    // Vector for semantic search (no attribute needed - framework detects float[] automatically)
    public float[]? Embedding { get; set; }

    // User interactions
    public int ViewCount { get; set; }
    public bool IsFavorite { get; set; }
    public int Rating { get; set; } // 0-5 stars

    // Processing
    public ProcessingStatus ProcessingStatus { get; set; } = ProcessingStatus.Pending;

    /// <summary>
    /// Converts photo metadata to embedding text for semantic search.
    /// Called automatically by [Embedding] attribute lifecycle hook.
    /// Prioritizes structured AI analysis, falls back to legacy fields.
    /// </summary>
    public string ToEmbeddingText()
    {
        var parts = new List<string>();

        // Structured AI analysis first (best semantic content)
        if (AiAnalysis != null)
        {
            parts.Add(AiAnalysis.ToEmbeddingText());
        }

        // Fallback to legacy fields if no structured analysis
        if (!string.IsNullOrEmpty(OriginalFileName))
            parts.Add($"Filename: {OriginalFileName}");

        if (AutoTags.Any())
            parts.Add($"Tags: {string.Join(", ", AutoTags)}");

        if (!string.IsNullOrEmpty(MoodDescription))
            parts.Add($"Mood: {MoodDescription}");

        if (!string.IsNullOrEmpty(CameraModel))
            parts.Add($"Camera: {CameraModel}");

        if (Location != null)
            parts.Add($"Location: {Location.Latitude}, {Location.Longitude}");

        return string.Join("\n", parts);
    }
}

public class GpsCoordinates
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double? Altitude { get; set; }
}
