namespace Koan.Services.Translation.Models;

/// <summary>
/// Result of a translation operation.
/// </summary>
public class TranslationResult
{
    /// <summary>
    /// Original text.
    /// </summary>
    public string OriginalText { get; set; } = "";

    /// <summary>
    /// Translated text.
    /// </summary>
    public string TranslatedText { get; set; } = "";

    /// <summary>
    /// Detected source language (if auto-detect was used).
    /// </summary>
    public string DetectedSourceLanguage { get; set; } = "";

    /// <summary>
    /// Target language.
    /// </summary>
    public string TargetLanguage { get; set; } = "";

    /// <summary>
    /// Confidence score (0.0 - 1.0).
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// Model used for translation.
    /// </summary>
    public string? Model { get; set; }
}
