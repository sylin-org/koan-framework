namespace Koan.AI.Contracts.Models;

/// <summary>
/// Desired output format for OCR results.
/// </summary>
public enum OcrFormat
{
    /// <summary>Raw extracted text, preserving original formatting.</summary>
    PlainText,

    /// <summary>Markdown-formatted output preserving headings, lists, and structure.</summary>
    Markdown,

    /// <summary>Structured JSON with regions, confidence scores, and bounding boxes.</summary>
    Structured
}
