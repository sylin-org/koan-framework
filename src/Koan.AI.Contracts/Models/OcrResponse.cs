namespace Koan.AI.Contracts.Models;

/// <summary>
/// Response from an OCR operation.
/// </summary>
public record OcrResponse
{
    /// <summary>Extracted text content.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Format of the returned text.</summary>
    public OcrFormat Format { get; init; } = OcrFormat.PlainText;

    /// <summary>Model used for recognition.</summary>
    public string? Model { get; init; }

    /// <summary>Overall confidence score (0.0–1.0), if available.</summary>
    public double? Confidence { get; init; }

    /// <summary>Detected text regions with positions, if format is Structured.</summary>
    public IReadOnlyList<OcrRegion>? Regions { get; init; }
}

/// <summary>
/// A detected text region within an image.
/// </summary>
public record OcrRegion
{
    /// <summary>Text content of this region.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Confidence score for this region (0.0–1.0).</summary>
    public double Confidence { get; init; }

    /// <summary>Bounding box: X coordinate (pixels from left).</summary>
    public int X { get; init; }

    /// <summary>Bounding box: Y coordinate (pixels from top).</summary>
    public int Y { get; init; }

    /// <summary>Bounding box width in pixels.</summary>
    public int Width { get; init; }

    /// <summary>Bounding box height in pixels.</summary>
    public int Height { get; init; }
}
