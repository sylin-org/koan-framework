using Koan.AI.Contracts.Models;

namespace Koan.Rag.Abstractions;

/// <summary>
/// Input to a content adapter: the raw content plus contextual hints.
/// </summary>
public sealed record ContentExtractionRequest
{
    /// <summary>File path (if available). Used for extension-based routing.</summary>
    public string? FilePath { get; init; }

    /// <summary>Raw file bytes. Always populated.</summary>
    public required byte[] Bytes { get; init; }

    /// <summary>Detected or declared modality.</summary>
    public required Modality Modality { get; init; }

    /// <summary>Document title (for contextual prefix generation).</summary>
    public string? DocumentTitle { get; init; }

    /// <summary>Corpus directive (domain guidance for extraction).</summary>
    public string? Directive { get; init; }

    /// <summary>MIME type if known.</summary>
    public string? MimeType { get; init; }
}
