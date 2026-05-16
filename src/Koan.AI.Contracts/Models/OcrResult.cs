using System.Collections.Generic;

namespace Koan.AI.Contracts.Models;

/// <summary>
/// Rich result from an OCR operation, returned by <c>Client.OcrResult()</c>.
/// </summary>
public sealed record OcrResult
{
    /// <summary>Extracted text content.</summary>
    public string Text { get; init; } = "";

    /// <summary>Format of the returned text.</summary>
    public OcrFormat Format { get; init; } = OcrFormat.PlainText;

    /// <summary>Overall confidence score (0.0–1.0), if available.</summary>
    public double? Confidence { get; init; }

    /// <summary>Model that served the request.</summary>
    public string? Model { get; init; }

    /// <summary>Detected text regions with positions, if format is Structured.</summary>
    public IReadOnlyList<OcrRegion>? Regions { get; init; }
}
