using Koan.Data.Core.Model;
using Koan.Mcp;
using S16.PantryPal.Services;

namespace S16.PantryPal.Models;

/// <summary>
/// Photos used for AI-powered pantry item detection.
/// Stores detection results with bounding boxes and multi-candidate selections.
/// </summary>
[McpEntity(Name = "PantryPhoto", Description = "Photos with AI-detected pantry items and bounding boxes")]
public sealed class PantryPhoto : Entity<PantryPhoto>
{
    // ==========================================
    // Image Storage
    // ==========================================

    /// <summary>Koan.Storage path to original photo</summary>
    public string StoragePath { get; set; } = "";

    public string? ThumbnailPath { get; set; }
    public string OriginalFileName { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public long FileSizeBytes { get; set; }

    // ==========================================
    // Capture Metadata
    // ==========================================

    public string? UploadedBy { get; set; }
    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;

    /// <summary>How photo was captured: camera, upload, bulk</summary>
    public string CaptureMode { get; set; } = "camera";

    public string? DeviceInfo { get; set; }

    // ==========================================
    // AI Processing
    // ==========================================

    /// <summary>Processing status: pending, processing, completed, failed</summary>
    public string ProcessingStatus { get; set; } = "pending";

    public DateTime? ProcessedAt { get; set; }
    public string? ProcessingError { get; set; }

    // ==========================================
    // AI Results
    // ==========================================

    public PantryDetection[] Detections { get; set; } = Array.Empty<PantryDetection>();
    public int DetectionCount { get; set; }
    public int ProcessingTimeMs { get; set; }
    public VisionMetrics? Metrics { get; set; }

    // ==========================================
    // User Actions
    // ==========================================

    public int ItemsConfirmed { get; set; }
    public int ItemsRejected { get; set; }
    public int ItemsEdited { get; set; }
}

/// <summary>
/// Individual item detection within a photo with bounding box and multi-candidate support.
/// </summary>
public class PantryDetection
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    // ==========================================
    // Bounding Box (for UI highlighting)
    // ==========================================

    public BoundingBox BoundingBox { get; set; } = new();

    // ==========================================
    // Multi-Candidate Detection
    // ==========================================

    /// <summary>AI-provided alternatives for user selection</summary>
    public DetectionCandidate[] Candidates { get; set; } = Array.Empty<DetectionCandidate>();

    /// <summary>User's selected candidate or top AI suggestion</summary>
    public string SelectedCandidateId { get; set; } = "";

    // ==========================================
    // Natural Language Input
    // ==========================================

    /// <summary>User's natural language input (e.g., "5 lbs, expires in a week")</summary>
    public string? UserInput { get; set; }

    /// <summary>Parsed structured data from user input</summary>
    public ParsedItemData? ParsedData { get; set; }

    // ==========================================
    // Status Tracking
    // ==========================================

    /// <summary>Detection status: pending, confirmed, rejected, edited</summary>
    public string Status { get; set; } = "pending";

    /// <summary>Link to created PantryItem if confirmed</summary>
    public string? CreatedPantryItemId { get; set; }
}

/// <summary>
/// AI detection candidate for an item (provides alternatives for user selection).
/// </summary>
public class DetectionCandidate
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Item name (e.g., "chicken breast")</summary>
    public string Name { get; set; } = "";

    /// <summary>AI confidence score (0.0-1.0)</summary>
    public float Confidence { get; set; }

    // Optional metadata for smart defaults
    public string? Category { get; set; }
    public string? DefaultUnit { get; set; }
    public int? TypicalShelfLifeDays { get; set; }
    public string? ImageUrl { get; set; }
}

/// <summary>
/// Bounding box coordinates for UI highlighting.
/// </summary>
public class BoundingBox
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

/// <summary>
/// Parsed structured data from natural language input.
/// </summary>
public class ParsedItemData
{
    // Quantity parsing
    public decimal? Quantity { get; set; }
    public string? Unit { get; set; }
    public string? QuantityRawInput { get; set; }

    // Expiration parsing
    public DateTime? ExpiresAt { get; set; }
    public string? ExpirationRawInput { get; set; }
    public ExpirationParseConfidence Confidence { get; set; }

    // Parse metadata
    public bool WasParsed { get; set; }
    public string[]? ParseWarnings { get; set; }
}

/// <summary>
/// Confidence level for expiration date parsing.
/// </summary>
public enum ExpirationParseConfidence
{
    High,      // "2025-10-10" - exact date
    Medium,    // "next month" - relative date
    Low,       // "soon" - vague
    Unparsed   // Could not parse
}
