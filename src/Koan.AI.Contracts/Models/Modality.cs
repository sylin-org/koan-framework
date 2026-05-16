namespace Koan.AI.Contracts.Models;

/// <summary>
/// Content modality for AI operations that accept non-text input.
/// Used by Embed, Describe, Classify, Moderate, and other verbs
/// to route to modality-capable adapters.
/// </summary>
public enum Modality
{
    /// <summary>Text content. Default for string overloads.</summary>
    Text,

    /// <summary>Image content (JPEG, PNG, WebP, TIFF, SVG).</summary>
    Image,

    /// <summary>Audio content (WAV, MP3, FLAC, OGG, M4A).</summary>
    Audio,

    /// <summary>Video content (MP4, WebM, MOV).</summary>
    Video,

    /// <summary>Document content (PDF, DOCX). Adapter extracts content as needed.</summary>
    Document
}
