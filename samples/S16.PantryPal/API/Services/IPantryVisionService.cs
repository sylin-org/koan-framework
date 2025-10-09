using S16.PantryPal.Models;

namespace S16.PantryPal.Services;

/// <summary>
/// AI vision service for pantry item detection from photos.
/// Integrates with Koan.AI for object detection, OCR, and barcode scanning.
/// </summary>
public interface IPantryVisionService
{
    /// <summary>
    /// Process uploaded pantry photo and detect items with bounding boxes.
    /// </summary>
    Task<VisionProcessingResult> ProcessPhotoAsync(
        string photoId,
        Stream imageStream,
        VisionProcessingOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Learn from user corrections to improve future detections.
    /// </summary>
    Task LearnFromCorrectionAsync(
        string originalName,
        string correctedName,
        string? correctedQuantity,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Options for vision processing customization.
/// </summary>
public class VisionProcessingOptions
{
    public bool DetectQuantities { get; set; } = true;
    public bool DetectExpirationDates { get; set; } = true;
    public bool DetectBarcodes { get; set; } = true;
    public bool DetectBrands { get; set; } = true;

    public float MinConfidenceThreshold { get; set; } = 0.5f;
    public int MaxDetectionsPerPhoto { get; set; } = 20;

    public string? UserId { get; set; }
}

/// <summary>
/// Result of vision processing operation.
/// </summary>
public class VisionProcessingResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }

    public PantryDetection[] Detections { get; set; } = Array.Empty<PantryDetection>();
    public int ProcessingTimeMs { get; set; }

    public VisionMetrics Metrics { get; set; } = new();
}

/// <summary>
/// Metrics about vision processing quality.
/// </summary>
public class VisionMetrics
{
    public int ItemsDetected { get; set; }
    public int HighConfidenceItems { get; set; }    // >0.9
    public int MediumConfidenceItems { get; set; }  // 0.7-0.9
    public int LowConfidenceItems { get; set; }     // <0.7

    public int ExpirationDatesDetected { get; set; }
    public int BarcodesDetected { get; set; }
}
