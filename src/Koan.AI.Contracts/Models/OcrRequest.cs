namespace Koan.AI.Contracts.Models;

/// <summary>
/// Request payload for OCR operations.
/// </summary>
public record OcrRequest
{
    /// <summary>Image data as byte array.</summary>
    public required byte[] Image { get; init; }

    /// <summary>MIME type of the image (e.g., "image/png", "image/jpeg").</summary>
    public string? MimeType { get; init; }

    /// <summary>Language hint for recognition (e.g., "en", "ja").</summary>
    public string? Language { get; init; }

    /// <summary>Desired output format.</summary>
    public OcrFormat Format { get; init; } = OcrFormat.PlainText;

    /// <summary>Model to use for recognition.</summary>
    public string? Model { get; init; }

    /// <summary>
    /// Internal property set by router to inject member URL into adapter.
    /// </summary>
    public string? InternalConnectionString { get; set; }
}
