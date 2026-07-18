using Koan.Media;
using Koan.Storage;
using Koan.Data.Vector.Abstractions;
using Koan.Data.AI.Attributes;
using Koan.Data.Core.Relationships;
using Koan.Data.Access;

namespace SnapVault.Models;

/// <summary>
/// One stored photo original plus its capture, organization, enrichment, and studio-interaction metadata.
/// </summary>
[StorageBinding(Profile = "cold", Container = "photos")]
// Stored and query embeddings must use the same vector space.
[Embedding(
    Policy = EmbeddingPolicy.AllStrings,
    Async = true,
    MaxTokens = 8191,
    Version = 2,
    Model = "nomic-embed-text",
    Exclude = ["EventId", "InferredStyleId"])]
// A constrained client sees only photos whose globally unique EventId appears in an "event:<id>" grant.
[AccessScoped("EventId", "event:")]
public class PhotoAsset : MediaEntity<PhotoAsset>
{
    // Event relationship
    [Parent(typeof(Event))]
    public string EventId { get; set; } = "";
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CapturedAt { get; set; } // From EXIF

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
    public AiAnalysis? AiAnalysis { get; set; } // Structured AI analysis (tags, summary, facts)

    // Smart-mode classification cache. Callers name the parent type because PhotoAsset has two parents.
    [Parent(typeof(AnalysisStyle))]
    public string? InferredStyleId { get; set; } // FK to AnalysisStyle (detected style)
    public DateTime? InferredAt { get; set; } // When classification was performed

    // Koan recognizes float[] as the semantic-search vector.
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
