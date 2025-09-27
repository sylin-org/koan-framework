using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Koan.AI.Contracts;
using Koan.AI.Contracts.Models;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using S13.DocMind.Infrastructure;
using S13.DocMind.Models;
using Microsoft.Extensions.DependencyInjection;

namespace S13.DocMind.Services;

public sealed class TextExtractionService : ITextExtractionService
{
    private readonly ILogger<TextExtractionService> _logger;
    private readonly IAi? _ai;
    private readonly DocMindOptions _options;
    private readonly IDocumentStorage _storage;

    public TextExtractionService(IServiceProvider serviceProvider, IOptions<DocMindOptions> options, IDocumentStorage storage, ILogger<TextExtractionService> logger)
    {
        _logger = logger;
        _options = options.Value;
        _storage = storage;
        _ai = serviceProvider.GetService<IAi>();
    }

    public async Task<DocumentExtractionResult> ExtractAsync(SourceDocument document, CancellationToken cancellationToken)
    {
        if (document is null) throw new ArgumentNullException(nameof(document));
        var (path, isTemp) = await EnsureLocalCopyAsync(document, cancellationToken).ConfigureAwait(false);

        try
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();
            string text;
            string? language = null;
            double? visionConfidence = null;
            IReadOnlyDictionary<string, object?> ocrDiagnostics = new Dictionary<string, object?>();
            int pageCount = 1;
            var containsImages = false;

            switch (extension)
            {
                case ".pdf":
                    (text, pageCount) = ExtractPdf(path);
                    break;
                case ".docx":
                    text = ExtractDocx(path);
                    break;
                case ".png":
                case ".jpg":
                case ".jpeg":
                case ".gif":
                case ".bmp":
                case ".webp":
                    containsImages = true;
                    var ocr = await DescribeImageAsync(path, document, cancellationToken).ConfigureAwait(false);
                    text = ocr.Text;
                    language = ocr.Language;
                    visionConfidence = ocr.Confidence;
                    ocrDiagnostics = ocr.Diagnostics;
                    break;
                default:
                    text = await File.ReadAllTextAsync(path, cancellationToken);
                    break;
            }

            var wordCount = CountWords(text);
            var chunks = BuildChunks(text, _options.Processing.ChunkSizeTokens);
            var diagnostics = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["source.extension"] = extension,
                ["source.contentType"] = document.ContentType,
                ["wordCount"] = wordCount,
                ["pageCount"] = pageCount,
                ["containsImages"] = containsImages
            };

            if (!string.IsNullOrWhiteSpace(language))
            {
                diagnostics["language"] = language;
            }

            if (visionConfidence.HasValue)
            {
                diagnostics["ocr.confidence"] = visionConfidence.Value;
            }

            foreach (var kvp in ocrDiagnostics)
            {
                diagnostics[$"ocr.{kvp.Key}"] = kvp.Value;
            }

            return new DocumentExtractionResult(text, chunks, wordCount, pageCount, containsImages, diagnostics, language);
        }
        finally
        {
            if (isTemp)
            {
                TryDelete(path);
            }
        }
    }

    private async Task<(string Path, bool Temporary)> EnsureLocalCopyAsync(SourceDocument document, CancellationToken cancellationToken)
    {
        if (document.Storage.TryResolvePhysicalPath(out var existing) && File.Exists(existing))
        {
            return (existing, false);
        }

        var extension = Path.GetExtension(document.DisplayName ?? document.FileName);
        var tempPath = Path.Combine(Path.GetTempPath(), $"docmind-{Guid.NewGuid():N}{extension}");

        await using var source = await _storage.OpenReadAsync(document.Storage, cancellationToken).ConfigureAwait(false);
        await using (var destination = File.Create(tempPath))
        {
            await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
        }

        return (tempPath, true);
    }

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Unable to delete temporary extraction file {Path}", path);
        }
    }

    private static (string Text, int PageCount) ExtractPdf(string path)
    {
        var builder = new StringBuilder();
        using var pdf = PdfDocument.Open(path);
        foreach (Page page in pdf.GetPages())
        {
            builder.AppendLine(page.Text);
            builder.AppendLine();
        }
        return (builder.ToString(), pdf.NumberOfPages);
    }

    private static string ExtractDocx(string path)
    {
        var builder = new StringBuilder();
        using var doc = WordprocessingDocument.Open(path, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is not null)
        {
            foreach (var text in body.Descendants<DocumentFormat.OpenXml.Wordprocessing.Text>())
            {
                builder.Append(text.Text);
                builder.Append(' ');
            }
        }
        return builder.ToString();
    }

    private async Task<OcrExtraction> DescribeImageAsync(string path, SourceDocument document, CancellationToken cancellationToken)
    {
        if (_ai is null)
        {
            _logger.LogInformation("AI provider not configured; using OCR placeholder for {Path}", path);
            return new OcrExtraction(
                Text: $"Image placeholder for {Path.GetFileName(path)}",
                Language: null,
                Confidence: null,
                Diagnostics: new Dictionary<string, object?>
                {
                    ["provider"] = "placeholder",
                    ["reason"] = "ai.unavailable"
                });
        }

        try
        {
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            var base64 = Convert.ToBase64String(bytes);
            var extension = Path.GetExtension(path).Trim('.').ToLowerInvariant();
            var model = _options.Ai.VisionModel ?? _options.Ai.DefaultModel;

            var request = new AiChatRequest
            {
                Model = model,
                Options = new AiPromptOptions
                {
                    Temperature = 0.1,
                    MaxOutputTokens = 900
                },
                Messages =
                {
                    new AiMessage("system", "You are a meticulous OCR and visual analysis assistant. Respond ONLY with a compact JSON object containing fields: text, language (ISO-639-1), confidence (0-1), keyPhrases (array), summary, and diagnostics (object with width,height,dominantColors?)."),
                    new AiMessage("user", $"Analyze the following {document.ContentType} image and extract structured findings. Return JSON.\nbase64://{extension}:{base64}")
                }
            };

            var response = await _ai.PromptAsync(request, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(response.Text))
            {
                return new OcrExtraction(
                    Text: $"Image placeholder for {Path.GetFileName(path)}",
                    Language: null,
                    Confidence: null,
                    Diagnostics: new Dictionary<string, object?>
                    {
                        ["provider"] = model,
                        ["reason"] = "empty-response"
                    });
            }

            using var doc = JsonDocument.Parse(response.Text);
            var root = doc.RootElement;
            var text = root.TryGetProperty("text", out var textElement) ? textElement.GetString() ?? string.Empty : string.Empty;
            var language = root.TryGetProperty("language", out var langElement) ? langElement.GetString() : null;
            double? confidence = null;
            if (root.TryGetProperty("confidence", out var confElement) &&
                confElement.TryGetDouble(out var confValue))
            {
                confidence = Math.Clamp(confValue, 0, 1);
            }

            var diagnostics = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["model"] = response.Model ?? model,
                ["tokensIn"] = response.TokensIn,
                ["tokensOut"] = response.TokensOut
            };

            if (root.TryGetProperty("summary", out var summaryElement))
            {
                diagnostics["summary"] = summaryElement.GetString();
            }

            if (root.TryGetProperty("keyPhrases", out var keyPhraseElement) && keyPhraseElement.ValueKind == JsonValueKind.Array)
            {
                diagnostics["keyPhrases"] = keyPhraseElement.EnumerateArray()
                    .Select(item => item.GetString())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .ToArray();
            }

            if (root.TryGetProperty("diagnostics", out var diagElement) && diagElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in diagElement.EnumerateObject())
                {
                    diagnostics[property.Name] = ConvertJsonValue(property.Value);
                }
            }

            return new OcrExtraction(text.Trim(), language, confidence, diagnostics);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Vision OCR returned invalid JSON for {Path}", path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Vision OCR failed for {Path}", path);
        }

        return new OcrExtraction(
            Text: $"Image placeholder for {Path.GetFileName(path)}",
            Language: null,
            Confidence: null,
            Diagnostics: new Dictionary<string, object?>
            {
                ["provider"] = _options.Ai.VisionModel ?? "unknown",
                ["reason"] = "exception"
            });
    }

    private static int CountWords(string text)
        => string.IsNullOrWhiteSpace(text) ? 0 : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    private static IReadOnlyList<ExtractedChunk> BuildChunks(string text, int chunkTokens)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<ExtractedChunk>();
        }

        chunkTokens = Math.Max(200, chunkTokens);
        var approxChars = chunkTokens * 4; // Rough heuristic: 1 token ~= 4 chars
        var paragraphs = text.Split(["\r\n\r\n", "\n\n"], StringSplitOptions.RemoveEmptyEntries);
        var chunks = new List<ExtractedChunk>();
        var builder = new StringBuilder();
        var index = 0;
        foreach (var paragraph in paragraphs)
        {
            if (builder.Length + paragraph.Length > approxChars && builder.Length > 0)
            {
                chunks.Add(CreateChunk(index++, builder.ToString()));
                builder.Clear();
            }
            builder.AppendLine(paragraph.Trim());
            builder.AppendLine();
        }

        if (builder.Length > 0)
        {
            chunks.Add(CreateChunk(index++, builder.ToString()));
        }

        if (chunks.Count == 0)
        {
            chunks.Add(CreateChunk(0, text));
        }

        return chunks;
    }

    private static ExtractedChunk CreateChunk(int index, string content)
    {
        var summary = content.Length <= 160 ? content.Trim() : content[..160].Trim() + "…";
        var metadata = new Dictionary<string, object?>
        {
            ["order"] = index,
            ["wordCount"] = CountWords(content),
            ["characterCount"] = content.Length
        };

        return new ExtractedChunk(index, content.Trim(), summary, metadata);
    }

    private static object? ConvertJsonValue(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.Number when element.TryGetDouble(out var d) => d,
            JsonValueKind.String => element.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonValue).ToArray(),
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(p => p.Name, p => ConvertJsonValue(p.Value), StringComparer.OrdinalIgnoreCase),
            _ => element.ToString()
        };

    private sealed record OcrExtraction(
        string Text,
        string? Language,
        double? Confidence,
        IReadOnlyDictionary<string, object?> Diagnostics);
}
