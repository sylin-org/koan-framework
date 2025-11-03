using Koan.AI;
using Koan.AI.Contracts.Options;
using Koan.ServiceMesh.Abstractions;
using Koan.Service.Translation.Models;
using Microsoft.Extensions.Logging;

namespace Koan.Service.Translation;

/// <summary>
/// Translation service using AI-powered translation.
/// Supports auto-discovery and dual deployment (in-process or container).
/// </summary>
[KoanService(
    "translation",
    DisplayName = "Translation Service",
    Description = "AI-powered translation service supporting multiple languages",
    Port = 8080,
    HeartbeatIntervalSeconds = 30,
    StaleThresholdSeconds = 120,
    ContainerImage = "koan/service-translation",
    DefaultTag = "latest")]
public class TranslationService
{
    private readonly ILogger<TranslationService> _logger;

    public TranslationService(ILogger<TranslationService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Translate text from one language to another.
    /// </summary>
    [KoanCapability("translate")]
    public async Task<TranslationResult> Translate(
        TranslationOptions options,
        CancellationToken ct = default)
    {
        // Normalize source language: null/empty/whitespace -> "auto"
        if (string.IsNullOrWhiteSpace(options.SourceLanguage))
        {
            options.SourceLanguage = "auto";
        }

        _logger.LogInformation(
            "Translating text to {TargetLanguage} (source: {SourceLanguage}, length: {Length} chars)",
            options.TargetLanguage,
            options.SourceLanguage,
            options.Text?.Length ?? 0);

        try
        {
            // Detect source language if auto (before chunking)
            var detectedLanguage = options.SourceLanguage;
            if (options.SourceLanguage == "auto")
            {
                // Use first chunk for language detection
                var sampleText = options.Text.Length > 500
                    ? options.Text.Substring(0, 500)
                    : options.Text;
                var detectedResult = await DetectLanguage(sampleText, ct);
                detectedLanguage = detectedResult.DetectedLanguage;
                _logger.LogInformation("Detected source language: {Language}", detectedLanguage);
            }

            // Chunk large texts for better translation quality
            const int maxChunkSize = 4000; // Conservative limit for AI context
            var translatedText = options.Text.Length <= maxChunkSize
                ? await TranslateSingleChunk(options.Text, detectedLanguage, options.TargetLanguage, options.Model, ct)
                : await TranslateChunked(options.Text, detectedLanguage, options.TargetLanguage, options.Model, ct);

            var result = new TranslationResult
            {
                OriginalText = options.Text,
                TranslatedText = translatedText,
                DetectedSourceLanguage = detectedLanguage,
                TargetLanguage = options.TargetLanguage,
                Confidence = 0.95, // TODO: Calculate actual confidence
                Model = options.Model ?? "default"
            };

            _logger.LogInformation(
                "Translation completed: {OriginalLength} chars → {TranslatedLength} chars",
                options.Text.Length,
                translatedText.Length);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Translation failed for target language {TargetLanguage}", options.TargetLanguage);
            throw;
        }
    }

    private async Task<string> TranslateSingleChunk(
        string text,
        string sourceLanguage,
        string targetLanguage,
        string? model,
        CancellationToken ct)
    {
        var prompt = text; // Just the text, no instructions

        _logger.LogDebug("Translating single chunk ({Length} chars) from {SourceLanguage} to {TargetLanguage}", text.Length, sourceLanguage, targetLanguage);

        var chatOptions = new AiChatOptions
        {
            Message = prompt,
            SystemPrompt = $@"You are a professional literary translator tasked with translating from {GetLanguageName(sourceLanguage)} to {GetLanguageName(targetLanguage)}.

YOUR TASK:
Read the entire text carefully to understand its context, tone, and meaning. Then produce an accurate translation that preserves the author's intent.

TRANSLATION PRINCIPLES:
1. CONTEXTUAL ACCURACY: Each word's meaning comes from its context. Don't translate words in isolation.
2. PRESERVE TONE: Maintain whether the text is poetic, technical, casual, formal, humorous, etc.
3. SEMANTIC FIDELITY: The translation must mean the same thing as the original. No substitutions with unrelated concepts.
4. CULTURAL ADAPTATION: Idioms and cultural references should be adapted naturally for the target language while preserving meaning.
5. FORMATTING: Preserve all line breaks, paragraph structure, markdown, italics, bold, and other formatting exactly.
6. PROPER NOUNS: Keep names, places, and titles unless they have established translations in the target language.

OUTPUT FORMAT:
- Output ONLY the translated text
- NO introductions like ""Here is the translation"" or ""Translating from X to Y""
- NO explanations or commentary
- NO meta-text about your process
- Start immediately with the translated content

BEGIN:",
            Model = model
        };

        var rawTranslation = await Ai.Chat(chatOptions, ct);

        // Post-process to remove common AI artifacts
        return CleanTranslationOutput(rawTranslation, targetLanguage);
    }

    private static string CleanTranslationOutput(string translation, string targetLanguage)
    {
        var cleaned = translation.Trim();

        // Remove common prefixes that AI adds
        var unwantedPrefixes = new[]
        {
            "here is the translation:",
            "here's the translation:",
            "translation:",
            "translated text:",
            $"in {GetLanguageName(targetLanguage).ToLowerInvariant()}:",
            "the translation is:",
            "here it is:",
            "sure!",
            "certainly!",
            "of course!",
            "aquí está la traducción:",
            "la traducción es:",
            "voici la traduction:",
            "hier ist die übersetzung:",
        };

        foreach (var prefix in unwantedPrefixes)
        {
            if (cleaned.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned.Substring(prefix.Length).TrimStart();
                break;
            }
        }

        // Remove explanatory sentences at the beginning
        var lines = cleaned.Split('\n');
        var firstContentLine = 0;

        for (int i = 0; i < Math.Min(3, lines.Length); i++)
        {
            var line = lines[i].Trim().ToLowerInvariant();

            // Skip meta-commentary lines
            if (line.Contains("translation") ||
                line.Contains("traducción") ||
                line.Contains("traduction") ||
                line.Contains("übersetzung") ||
                line.Contains("i will") ||
                line.Contains("i'll") ||
                line.Contains("let me") ||
                line.Contains("provide") ||
                line.Contains("literal") ||
                line.Contains("poetic") ||
                line.Contains("symbolic") ||
                line.Contains("style") ||
                line.Contains("language is") ||
                line.Contains("from") && line.Contains("to"))
            {
                firstContentLine = i + 1;
                continue;
            }

            break;
        }

        if (firstContentLine > 0 && firstContentLine < lines.Length)
        {
            cleaned = string.Join("\n", lines.Skip(firstContentLine));
        }

        return cleaned.Trim();
    }

    private async Task<string> TranslateChunked(
        string text,
        string sourceLanguage,
        string targetLanguage,
        string? model,
        CancellationToken ct)
    {
        const int maxChunkSize = 4000;
        const int overlapSize = 200; // Context overlap between chunks
        var chunks = SplitIntoChunks(text, maxChunkSize);

        _logger.LogInformation(
            "Translating large text in {ChunkCount} chunks with context overlap",
            chunks.Count);

        var translatedChunks = new List<string>();
        string? previousContext = null;

        for (int i = 0; i < chunks.Count; i++)
        {
            _logger.LogDebug("Translating chunk {Current}/{Total}", i + 1, chunks.Count);

            // Add context from previous chunk for continuity
            var chunkToTranslate = chunks[i];
            if (previousContext != null && chunks[i].Length > overlapSize)
            {
                // Include last part of previous chunk as context
                chunkToTranslate = $"[Context from previous section: {previousContext}]\n\n{chunks[i]}";
            }

            var translatedChunk = await TranslateSingleChunk(
                chunkToTranslate,
                sourceLanguage,
                targetLanguage,
                model,
                ct);

            // Remove the context marker if AI included it
            if (previousContext != null)
            {
                translatedChunk = translatedChunk
                    .Replace("[Context from previous section:", "")
                    .Replace("[Contexto de la sección anterior:", "")
                    .Replace("[Contexte de la section précédente:", "")
                    .TrimStart('[', ' ', '\n');

                // Find where the actual new content starts (after context)
                var lines = translatedChunk.Split('\n');
                var startIndex = 0;
                for (int j = 0; j < Math.Min(5, lines.Length); j++)
                {
                    if (lines[j].Contains("]") || lines[j].Trim().Length < 10)
                    {
                        startIndex = j + 1;
                    }
                    else
                    {
                        break;
                    }
                }
                if (startIndex > 0 && startIndex < lines.Length)
                {
                    translatedChunk = string.Join("\n", lines.Skip(startIndex));
                }
            }

            translatedChunks.Add(translatedChunk.Trim());

            // Save last portion as context for next chunk
            if (i < chunks.Count - 1 && chunks[i].Length > overlapSize)
            {
                previousContext = chunks[i].Substring(chunks[i].Length - overlapSize);
            }
        }

        // Rejoin chunks with proper spacing
        return string.Join("\n\n", translatedChunks);
    }

    private static List<string> SplitIntoChunks(string text, int maxChunkSize)
    {
        var chunks = new List<string>();

        // Split by paragraphs first to maintain context
        var paragraphs = text.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.None);

        var currentChunk = new List<string>();
        var currentLength = 0;

        foreach (var paragraph in paragraphs)
        {
            var paragraphLength = paragraph.Length;

            // If single paragraph exceeds max, split it further
            if (paragraphLength > maxChunkSize)
            {
                // Flush current chunk if any
                if (currentChunk.Count > 0)
                {
                    chunks.Add(string.Join("\n\n", currentChunk));
                    currentChunk.Clear();
                    currentLength = 0;
                }

                // Split large paragraph by sentences
                var sentences = paragraph.Split(new[] { ". ", ".\n", "! ", "!\n", "? ", "?\n" }, StringSplitOptions.None);
                var sentenceChunk = new List<string>();
                var sentenceLength = 0;

                foreach (var sentence in sentences)
                {
                    if (sentenceLength + sentence.Length > maxChunkSize && sentenceChunk.Count > 0)
                    {
                        chunks.Add(string.Join(". ", sentenceChunk) + ".");
                        sentenceChunk.Clear();
                        sentenceLength = 0;
                    }
                    sentenceChunk.Add(sentence);
                    sentenceLength += sentence.Length;
                }

                if (sentenceChunk.Count > 0)
                {
                    chunks.Add(string.Join(". ", sentenceChunk) + ".");
                }
            }
            else if (currentLength + paragraphLength > maxChunkSize)
            {
                // Current chunk is full, start new one
                chunks.Add(string.Join("\n\n", currentChunk));
                currentChunk = new List<string> { paragraph };
                currentLength = paragraphLength;
            }
            else
            {
                // Add to current chunk
                currentChunk.Add(paragraph);
                currentLength += paragraphLength + 2; // +2 for \n\n
            }
        }

        // Add remaining chunk
        if (currentChunk.Count > 0)
        {
            chunks.Add(string.Join("\n\n", currentChunk));
        }

        return chunks;
    }

    /// <summary>
    /// Get list of supported languages.
    /// </summary>
    [KoanCapability("get-languages")]
    public Task<SupportedLanguage[]> GetLanguages(CancellationToken ct = default)
    {
        var languages = new[]
        {
            new SupportedLanguage("en", "English"),
            new SupportedLanguage("es", "Spanish"),
            new SupportedLanguage("fr", "French"),
            new SupportedLanguage("de", "German"),
            new SupportedLanguage("it", "Italian"),
            new SupportedLanguage("pt", "Portuguese"),
            new SupportedLanguage("ru", "Russian"),
            new SupportedLanguage("ja", "Japanese"),
            new SupportedLanguage("zh", "Chinese"),
            new SupportedLanguage("ko", "Korean"),
            new SupportedLanguage("ar", "Arabic"),
            new SupportedLanguage("hi", "Hindi")
        };

        return Task.FromResult(languages);
    }

    /// <summary>
    /// Detect the language of the provided text.
    /// </summary>
    [KoanCapability("detect-language")]
    public async Task<LanguageDetectionResult> DetectLanguage(
        string text,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Detecting language for {Length} chars", text.Length);

        try
        {
            // Use a sample if text is very long
            var sampleText = text.Length > 500 ? text.Substring(0, 500) : text;

            var chatOptions = new AiChatOptions
            {
                Message = sampleText,
                SystemPrompt = "You are a language detection system. Identify the language and output ONLY the two-letter ISO 639-1 code in lowercase (examples: en, es, fr, de, ja, zh). Output nothing else - no explanations, no punctuation, just the code."
            };

            var languageCode = await Ai.Chat(chatOptions, ct);

            // Extract just the code if AI added extra text
            var cleanCode = ExtractLanguageCode(languageCode);

            var result = new LanguageDetectionResult
            {
                Text = text,
                DetectedLanguage = cleanCode,
                Confidence = 0.90 // TODO: Calculate actual confidence
            };

            _logger.LogInformation("Language detected: {Language}", result.DetectedLanguage);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Language detection failed");
            throw;
        }
    }

    private static string ExtractLanguageCode(string aiResponse)
    {
        // Clean up the response and extract just the language code
        var cleaned = aiResponse.Trim().ToLowerInvariant();

        // Remove common punctuation and quotes
        cleaned = cleaned.Trim('"', '\'', '.', ',', '!', '?', ' ', '\n', '\r');

        // If response contains multiple words, try to extract the code
        if (cleaned.Contains(' '))
        {
            // Look for patterns like "the language is: en" or "en (english)"
            var words = cleaned.Split(new[] { ' ', ':', '(', ')' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var word in words)
            {
                // ISO 639-1 codes are exactly 2 letters
                if (word.Length == 2 && word.All(char.IsLetter))
                {
                    return word;
                }
            }
        }

        // If it's already clean (2 letters), return it
        if (cleaned.Length == 2 && cleaned.All(char.IsLetter))
        {
            return cleaned;
        }

        // Fallback: take first 2 letters if they're alphabetic
        if (cleaned.Length >= 2 && cleaned.Take(2).All(char.IsLetter))
        {
            return new string(cleaned.Take(2).ToArray());
        }

        // Last resort fallback
        return "en";
    }

    private static string GetLanguageName(string code)
    {
        // Map common language codes to names
        return code.ToLowerInvariant() switch
        {
            "en" => "English",
            "es" => "Spanish",
            "fr" => "French",
            "de" => "German",
            "it" => "Italian",
            "pt" => "Portuguese",
            "ru" => "Russian",
            "ja" => "Japanese",
            "zh" => "Chinese",
            "ko" => "Korean",
            "ar" => "Arabic",
            "hi" => "Hindi",
            _ => code.ToUpperInvariant()
        };
    }
}

/// <summary>
/// Supported language information.
/// </summary>
public record SupportedLanguage(string Code, string Name);

/// <summary>
/// Result of language detection.
/// </summary>
public class LanguageDetectionResult
{
    /// <summary>
    /// Text that was analyzed.
    /// </summary>
    public string Text { get; set; } = "";

    /// <summary>
    /// Detected language code (ISO 639-1).
    /// </summary>
    public string DetectedLanguage { get; set; } = "";

    /// <summary>
    /// Confidence score (0.0 - 1.0).
    /// </summary>
    public double Confidence { get; set; }
}
