namespace Koan.Data.AI.Attributes;

/// <summary>
/// Marks a MediaEntity for automatic AI processing on upload/change.
/// Mirrors how <see cref="EmbeddingAttribute"/> auto-embeds on save,
/// but for media-specific AI: vision analysis, OCR, transcription,
/// classification, and structured extraction.
///
/// <code>
/// [MediaAnalysis(Analysis = MediaAnalysis.Describe | MediaAnalysis.Ocr, Async = true)]
/// [Embedding]
/// public class PhotoAsset : MediaEntity&lt;PhotoAsset&gt;
/// {
///     public string? AiDescription { get; set; }   // Auto-populated by Describe
///     public string? OcrText { get; set; }         // Auto-populated by Ocr
///     public float[]? Embedding { get; set; }      // Auto-populated by [Embedding]
/// }
/// </code>
///
/// Pipeline: Upload → Store → [MediaAnalysis] → [Embedding] → Save atomically.
/// Analysis results feed into [Embedding] text automatically.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class MediaAnalysisAttribute : Attribute
{
    /// <summary>
    /// Which AI operations to perform on upload/change.
    /// Can be combined with flags: <c>MediaAnalysis.Describe | MediaAnalysis.Ocr</c>
    /// </summary>
    public MediaAnalysis Analysis { get; set; } = MediaAnalysis.Describe;

    /// <summary>
    /// Property name for vision description output.
    /// Default: convention-detected (AiDescription, Description, Summary).
    /// </summary>
    public string? DescriptionProperty { get; set; }

    /// <summary>
    /// Property name for OCR text output.
    /// Default: convention-detected (OcrText, ExtractedText, Text).
    /// </summary>
    public string? OcrTextProperty { get; set; }

    /// <summary>
    /// Property name for audio/video transcription output.
    /// Default: convention-detected (Transcript, Transcription).
    /// </summary>
    public string? TranscriptProperty { get; set; }

    /// <summary>
    /// Property name for classification output.
    /// Default: convention-detected (Category, Classification, MediaType).
    /// </summary>
    public string? ClassificationProperty { get; set; }

    /// <summary>
    /// Property name for structured extraction output (used with Extract mode).
    /// Default: convention-detected based on Prompt output type.
    /// </summary>
    public string? ExtractedDataProperty { get; set; }

    /// <summary>
    /// Process asynchronously in a background worker (default: true).
    /// When true, entity is saved immediately after upload; analysis is queued.
    /// When false, analysis completes before Save() returns.
    /// </summary>
    public bool Async { get; set; } = true;

    /// <summary>
    /// Named prompt to use for Extract mode (loaded from PromptEntry catalog).
    /// Also used for Describe/Classify if you want to customize the analysis prompt.
    /// </summary>
    public string? Prompt { get; set; }

    /// <summary>
    /// Schema version. Increment to trigger re-analysis of all entities of this type.
    /// Useful when: prompt improved, model upgraded, extraction schema changed.
    /// </summary>
    public int Version { get; set; } = 1;
}

/// <summary>
/// AI operations that can be performed on media content.
/// Combine with flags: <c>MediaAnalysis.Describe | MediaAnalysis.Ocr</c>
/// </summary>
[Flags]
public enum MediaAnalysis
{
    /// <summary>No analysis.</summary>
    None = 0,

    /// <summary>Vision: generate text description of image/video content.</summary>
    Describe = 1,

    /// <summary>OCR: extract visible text from images, PDFs, screenshots.</summary>
    Ocr = 2,

    /// <summary>Audio/Video: speech-to-text transcription.</summary>
    Transcribe = 4,

    /// <summary>Categorize content type (image type, document type, etc.).</summary>
    Classify = 8,

    /// <summary>Structured extraction via named Prompt (AI-0025) → typed property.</summary>
    Extract = 16,

    /// <summary>All analysis modes.</summary>
    All = Describe | Ocr | Transcribe | Classify | Extract
}
