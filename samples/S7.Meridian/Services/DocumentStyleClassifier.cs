using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Koan.AI;
using Koan.AI.Contracts.Options;
using Koan.Data.Core;
using Koan.Samples.Meridian.Infrastructure;
using Koan.Samples.Meridian.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Koan.Samples.Meridian.Services;

public interface IDocumentStyleClassifier
{
    Task<DocumentStyleClassification> ClassifyAsync(
        SourceDocument document,
        CancellationToken ct = default);
}

/// <summary>
/// Classifies documents by content style (Narrative, SparseForm, Dialogue, etc.)
/// to enable style-specific extraction strategies.
/// Results are cached in the SourceDocument entity.
/// </summary>
public sealed class DocumentStyleClassifier : IDocumentStyleClassifier
{
    private readonly MeridianOptions _options;
    private readonly ILogger<DocumentStyleClassifier> _logger;

    public DocumentStyleClassifier(IOptions<MeridianOptions> options, ILogger<DocumentStyleClassifier> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<DocumentStyleClassification> ClassifyAsync(
        SourceDocument document,
        CancellationToken ct = default)
    {
        // Get all available document styles for version checking
        var allStyles = await DocumentStyle.All(ct);
        var styles = allStyles.ToList();
        if (styles.Count == 0)
        {
            _logger.LogWarning("No DocumentStyles found in database - seeding may not have completed");
            throw new InvalidOperationException("No document styles available for classification");
        }

        // Check if already classified with current version
        if (!string.IsNullOrWhiteSpace(document.DocumentStyleCode))
        {
            var currentStyle = styles.FirstOrDefault(s => s.Code == document.DocumentStyleCode);
            var cachedVersion = document.DocumentStyleVersion ?? 1;

            // If style version matches, use cached classification
            if (currentStyle != null && currentStyle.Version == cachedVersion)
            {
                _logger.LogDebug("Document {DocumentId} already classified as {Style} v{Version} (confidence: {Confidence:F2})",
                    document.Id, document.DocumentStyleCode, cachedVersion, document.DocumentStyleConfidence);

                return new DocumentStyleClassification
                {
                    StyleCode = document.DocumentStyleCode,
                    StyleId = document.DocumentStyleId,
                    StyleVersion = cachedVersion,
                    Confidence = document.DocumentStyleConfidence,
                    Reasoning = document.DocumentStyleReason ?? "Previously classified"
                };
            }

            // Style version changed - re-classify
            _logger.LogInformation("Re-classifying document {DocumentId} - DocumentStyle {Style} version changed from {OldVersion} to {NewVersion}",
                document.Id, document.DocumentStyleCode, cachedVersion, currentStyle?.Version ?? 0);
        }

        // Build classification prompt
        var prompt = BuildClassificationPrompt(document, styles);

        // Call LLM
        var chatOptions = new AiChatOptions
        {
            Message = prompt,
            Model = _options.Facts.ExtractionModel,
            Temperature = 0.3, // Lower temperature for consistent classification
            MaxTokens = 0,
            ResponseFormat = "json"
        };

        string raw;
        try
        {
            raw = await Ai.Chat(chatOptions, ct);
            _logger.LogDebug("LLM document style classification response length: {Length} characters", raw.Length);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to classify document style for {DocumentId} with model {Model}",
                document.Id, _options.Facts.ExtractionModel);
            throw;
        }

        // Parse response
        var classification = ParseClassification(raw, styles);

        // Update document with classification
        document.DocumentStyleCode = classification.StyleCode;
        document.DocumentStyleId = classification.StyleId;
        document.DocumentStyleVersion = classification.StyleVersion;
        document.DocumentStyleConfidence = classification.Confidence;
        document.DocumentStyleReason = classification.Reasoning;
        document.DocumentStyleClassifiedAt = DateTime.UtcNow;
        document.UpdatedAt = DateTime.UtcNow;
        await document.Save(ct);

        _logger.LogInformation("Classified document {DocumentId} '{FileName}' as {Style} (confidence: {Confidence:F2})",
            document.Id, document.OriginalFileName, classification.StyleCode, classification.Confidence);

        return classification;
    }

    private string BuildClassificationPrompt(SourceDocument document, List<DocumentStyle> styles)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are classifying a document by its content style to enable appropriate fact extraction strategies.");
        sb.AppendLine();

        // Document preview (first 4000 characters for better classification accuracy)
        var preview = document.ExtractedText.Length > 4000
            ? document.ExtractedText.Substring(0, 4000) + "... [truncated]"
            : document.ExtractedText;

        sb.AppendLine("DOCUMENT TO CLASSIFY:");
        sb.AppendLine($"Filename: {document.OriginalFileName}");
        sb.AppendLine($"Pages: {document.PageCount}");
        sb.AppendLine();
        sb.AppendLine("DOCUMENT PREVIEW:");
        sb.AppendLine(preview);
        sb.AppendLine();
        sb.AppendLine("AVAILABLE DOCUMENT STYLES:");
        sb.AppendLine();

        int index = 1;
        foreach (var style in styles.OrderBy(s => s.Code))
        {
            sb.AppendLine($"{index}. {style.Code} - {style.Name}");
            sb.AppendLine($"   Description: {style.Description}");
            sb.AppendLine($"   Detection Hints:");
            foreach (var hint in style.DetectionHints)
            {
                sb.AppendLine($"   - {hint}");
            }

            if (style.SignalPhrases.Any())
            {
                sb.AppendLine($"   Signal Phrases: {string.Join(", ", style.SignalPhrases.Take(5))}");
            }

            sb.AppendLine();
            index++;
        }

        sb.AppendLine("TASK:");
        sb.AppendLine("Analyze the document preview and classify it into ONE of the document styles above.");
        sb.AppendLine();
        sb.AppendLine("Consider:");
        sb.AppendLine("- Overall document structure and format");
        sb.AppendLine("- Presence of signal phrases and patterns");
        sb.AppendLine("- Ratio of template/boilerplate to actual content");
        sb.AppendLine("- Communication style (narrative vs dialogue vs form)");
        sb.AppendLine();
        sb.AppendLine("OUTPUT FORMAT (strict JSON):");
        sb.AppendLine("{");
        sb.AppendLine("  \"styleCode\": \"SPARSE\",");
        sb.AppendLine("  \"confidence\": 0.85,");
        sb.AppendLine("  \"reasoning\": \"Document appears to be a vendor questionnaire with repetitive 'Question X:' patterns and many empty fields or checkbox options. Signal phrases like 'Request Type options include' and 'Questions cover' indicate form template structure with minimal filled responses.\"");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private DocumentStyleClassification ParseClassification(string rawResponse, List<DocumentStyle> styles)
    {
        try
        {
            // Strip markdown code fences if present (some LLMs wrap JSON in ```json blocks)
            var cleaned = rawResponse.Trim();
            if (cleaned.StartsWith("```"))
            {
                // Find first newline after opening fence
                var firstNewline = cleaned.IndexOf('\n');
                if (firstNewline > 0)
                {
                    cleaned = cleaned.Substring(firstNewline + 1);
                }

                // Remove closing fence if present
                if (cleaned.EndsWith("```"))
                {
                    cleaned = cleaned.Substring(0, cleaned.Length - 3);
                }

                cleaned = cleaned.Trim();
            }

            var json = JObject.Parse(cleaned);

            var styleCode = json.Value<string>("styleCode")?.Trim()?.ToUpperInvariant() ?? string.Empty;
            var confidence = json.Value<double?>("confidence") ?? 0.0;
            var reasoning = json.Value<string>("reasoning")?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(styleCode))
            {
                _logger.LogWarning("LLM returned no styleCode - falling back to NARRATIVE");
                styleCode = "NARRATIVE";
                confidence = 0.5;
                reasoning = "Default fallback - no style code returned";
            }

            // Find matching style
            var matchedStyle = styles.FirstOrDefault(s =>
                s.Code.Equals(styleCode, StringComparison.OrdinalIgnoreCase));

            if (matchedStyle == null)
            {
                _logger.LogWarning("LLM returned unknown styleCode '{StyleCode}' - falling back to NARRATIVE",
                    styleCode);
                matchedStyle = styles.FirstOrDefault(s => s.Code == "NARRATIVE")
                    ?? styles.First(); // Absolute fallback
                confidence = 0.5;
                reasoning = $"Unknown style code '{styleCode}' - using fallback";
            }

            return new DocumentStyleClassification
            {
                StyleCode = matchedStyle.Code,
                StyleId = matchedStyle.Id,
                StyleVersion = matchedStyle.Version,
                Confidence = Math.Clamp(confidence, 0.0, 1.0),
                Reasoning = reasoning
            };
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse LLM document style classification response");

            // Fallback to NARRATIVE
            var fallbackStyle = styles.FirstOrDefault(s => s.Code == "NARRATIVE") ?? styles.First();
            return new DocumentStyleClassification
            {
                StyleCode = fallbackStyle.Code,
                StyleId = fallbackStyle.Id,
                StyleVersion = fallbackStyle.Version,
                Confidence = 0.5,
                Reasoning = "Parse error - using fallback"
            };
        }
    }
}

/// <summary>
/// Result of document style classification.
/// </summary>
public sealed class DocumentStyleClassification
{
    public string StyleCode { get; set; } = string.Empty;
    public string? StyleId { get; set; }
    public int StyleVersion { get; set; }
    public double Confidence { get; set; }
    public string Reasoning { get; set; } = string.Empty;
}
