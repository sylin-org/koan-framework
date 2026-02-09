using Koan.AI.Contracts.Models;

namespace Koan.AI.Contracts.Options;

/// <summary>
/// Options for OCR (optical character recognition) requests.
/// </summary>
public sealed record OcrOptions
{
    /// <summary>Desired output format.</summary>
    public OcrFormat Format { get; init; } = OcrFormat.PlainText;

    /// <summary>Language hint for recognition (e.g., "en", "ja").</summary>
    public string? Language { get; init; }

    /// <summary>MIME type of the image (e.g., "image/png", "image/jpeg").</summary>
    public string? MimeType { get; init; }

    /// <summary>
    /// Override source for this request.
    /// Takes precedence over scope and routing configuration.
    /// </summary>
    public string? Source { get; init; }

    /// <summary>Override model for this request.</summary>
    public string? Model { get; init; }
}
