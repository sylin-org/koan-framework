using Koan.Media.Abstractions.Model;
using Koan.Storage.Infrastructure;
using Koan.Data.Vector.Abstractions;
using Koan.Data.AI.Attributes;
using Koan.Data.Core.Relationships;
using Koan.Data.Access;

namespace S6.SnapVault.Models;

/// <summary>
/// Full-resolution photo asset with complete metadata (stored in cold tier for cost optimization)
/// </summary>
[StorageBinding(Profile = "cold", Container = "photos")]
// Model MUST match the query-side embed model in PhotoProcessingService.SemanticSearch (same model = same vector
// space). nomic-embed-text is a dedicated embedding model — distinct from the vision chat model (Ollama DefaultModel),
// since one Ollama model can't both see images and embed text.
[Embedding(
    Policy = EmbeddingPolicy.AllStrings,
    Async = true,
    MaxTokens = 8191,
    Version = 2,
    Model = "nomic-embed-text",
    Exclude = ["EventId", "InferredStyleId"])]
// SEC-0008 data-layer access scoping: a CONSTRAINED subject (an invited guest) sees only photos whose EventId is in
// their "event:<id>" scope tokens — fail-closed on an absent subject. Studio operators run Subject.Unconstrained,
// ingest/AI jobs Subject.System(). Enforced on EVERY read (controller, service, job, SSE), not just the endpoint.
// INVARIANT: EventId must be a delimiter-free, globally-unique id (a GUID) — it is BOTH the scoped field and (as
// "event:<id>") the scope token; a slug/email would reintroduce cross-studio collisions and token-splits.
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

    // Smart mode classification cache (avoids repeated classification API calls).
    // [Parent] makes this a navigable relationship for GetRelatives. Two caveats for the step-5 relatives surface:
    // PhotoAsset now has TWO parents (Event + AnalysisStyle), so callers must use GetParent<AnalysisStyle>() (the
    // non-generic GetParent() throws on multiple parents); and AnalysisStyle is [HostScoped] while PhotoAsset is
    // tenant-scoped — validate cross-scope resolution + nullable (no inference yet) handling when that nav lands.
    [Parent(typeof(AnalysisStyle))]
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

    // Embedding text extraction handled by framework via [Embedding(Policy=AllStrings)].
    // All public string properties (excluding Id, EventId, InferredStyleId) are included by convention.
    // AiAnalysis, AutoTags, DetectedObjects contribute via their string representations.
}

public class GpsCoordinates
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double? Altitude { get; set; }
}
