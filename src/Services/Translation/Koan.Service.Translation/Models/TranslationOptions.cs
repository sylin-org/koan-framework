namespace Koan.Service.Translation.Models;

/// <summary>
/// Options for translation requests.
/// </summary>
public class TranslationOptions
{
    /// <summary>
    /// Text to translate.
    /// </summary>
    public string Text { get; set; } = "";

    /// <summary>
    /// Target language code (e.g., "es", "fr", "de").
    /// </summary>
    public string TargetLanguage { get; set; } = "";

    /// <summary>
    /// Source language code (optional, "auto" = auto-detect).
    /// </summary>
    public string SourceLanguage { get; set; } = "auto";

    /// <summary>
    /// AI model to use for translation (optional).
    /// </summary>
    public string? Model { get; set; }

    public TranslationOptions()
    {
    }

    public TranslationOptions(string text, string targetLanguage, string sourceLanguage = "auto")
    {
        Text = text;
        TargetLanguage = targetLanguage;
        SourceLanguage = sourceLanguage;
    }
}
