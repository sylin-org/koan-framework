using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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

public interface IDocumentClassifier
{
    Task<ClassificationResult> ClassifyAsync(SourceDocument document, CancellationToken ct);
}

public readonly record struct ClassificationResult(string TypeId, double Confidence, ClassificationMethod Method, int Version, string Reason);

public sealed class DocumentClassifier : IDocumentClassifier
{
    private readonly MeridianOptions _options;
    private readonly ILogger<DocumentClassifier> _logger;
    private readonly ConcurrentDictionary<string, SourceTypeSnapshot> _snapshots = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, float[]> _embeddingCache = new(StringComparer.OrdinalIgnoreCase);

    private sealed record SourceTypeSnapshot(
        int Version,
        DateTime UpdatedAt,
        string[] DescriptorHints,
        string[] SignalPhrases,
        string[] MimeTypesLower,
        bool SupportsManualSelection,
        int? ExpectedPageCountMin,
        int? ExpectedPageCountMax);

    public DocumentClassifier(IOptions<MeridianOptions> options, ILogger<DocumentClassifier> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ClassificationResult> ClassifyAsync(SourceDocument document, CancellationToken ct)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        var types = await SourceType.All(ct);
        if (types.Count == 0)
        {
            return new ClassificationResult(MeridianConstants.SourceTypes.Unspecified, 0.0, ClassificationMethod.Manual, 1, "No source types configured.");
        }

        if (document.ClassificationMethod == ClassificationMethod.Manual &&
            !string.IsNullOrWhiteSpace(document.ClassifiedTypeId))
        {
            var manualTypeId = document.ClassifiedTypeId!;
            var matched = types.FirstOrDefault(t => string.Equals(t.Id, manualTypeId, StringComparison.OrdinalIgnoreCase));
            var version = document.ClassifiedTypeVersion ?? matched?.Version ?? 1;
            var confidence = document.ClassificationConfidence > 0 ? document.ClassificationConfidence : 1.0;
            var reason = string.IsNullOrWhiteSpace(document.ClassificationReason)
                ? "User selected type"
                : document.ClassificationReason!;

            _logger.LogDebug("Document {DocumentId} honoring manual selection {TypeId}.", document.Id, manualTypeId);
            return new ClassificationResult(manualTypeId, confidence, ClassificationMethod.Manual, version, reason);
        }

        var text = document.ExtractedText ?? string.Empty;

        // Stage 1: Heuristic
        var heuristic = EvaluateHeuristics(document, text, types);
        if (heuristic.HasValue && heuristic.Value.Confidence >= _options.Classification.HeuristicConfidenceThreshold)
        {
            _logger.LogDebug("Document {DocumentId} classified via heuristics as {TypeId} ({Confidence:P0}). Reason: {Reason}",
                document.Id, heuristic.Value.TypeId, heuristic.Value.Confidence, heuristic.Value.Reason);
            return heuristic.Value;
        }

        // Stage 2: Vector similarity
        var vector = await EvaluateVectorSimilarityAsync(document, text, types, ct);
        if (vector.HasValue && vector.Value.Confidence >= _options.Classification.VectorConfidenceThreshold)
        {
            _logger.LogDebug("Document {DocumentId} classified via vector similarity as {TypeId} ({Confidence:P0}). Reason: {Reason}",
                document.Id, vector.Value.TypeId, vector.Value.Confidence, vector.Value.Reason);
            return vector.Value;
        }

        // Stage 3: LLM fallback
        var llm = await EvaluateLlmAsync(document, text, types, ct);
        if (llm.HasValue)
        {
            _logger.LogDebug("Document {DocumentId} classified via LLM as {TypeId} ({Confidence:P0}). Reason: {Reason}",
                document.Id, llm.Value.TypeId, llm.Value.Confidence, llm.Value.Reason);
            return llm.Value;
        }

        _logger.LogWarning("Document {DocumentId} could not be classified - using Unspecified fallback type.", document.Id);
        return new ClassificationResult(MeridianConstants.SourceTypes.Unspecified, 0.3, ClassificationMethod.Heuristic, 1, "Classification failed - using generic extraction strategy.");
    }

    private ClassificationResult? EvaluateHeuristics(SourceDocument document, string text, IReadOnlyList<SourceType> types)
    {
        const double DescriptorWeight = 0.55;
        const double SignalWeight = 0.25;
        const double PageWeight = 0.15;
        const double MimeWeight = 0.05;

        var best = default(ClassificationResult?);
        var textLower = NormalizeContent(text);
        var fileNameLower = NormalizeFileName(document.OriginalFileName);
        var mediaLower = document.MediaType?.ToLowerInvariant();

        foreach (var type in types)
        {
            var snapshot = GetSnapshot(type);
            double score = 0;
            double maxScore = 0;
            var reasons = new List<string>();

            if (snapshot.DescriptorHints.Length > 0)
            {
                maxScore += DescriptorWeight;
                var descriptorHits = CountPhraseHits(snapshot.DescriptorHints, textLower, fileNameLower);
                if (descriptorHits > 0)
                {
                    var ratio = descriptorHits / Math.Max(1.0, snapshot.DescriptorHints.Length);
                    score += DescriptorWeight * Math.Min(1.0, ratio);
                    reasons.Add($"Descriptor hints matched {descriptorHits}/{snapshot.DescriptorHints.Length}");
                }
            }

            if (snapshot.SignalPhrases.Length > 0)
            {
                maxScore += SignalWeight;
                var signalHits = CountPhraseHits(snapshot.SignalPhrases, textLower, fileNameLower);
                if (signalHits > 0)
                {
                    var ratio = signalHits / Math.Max(1.0, snapshot.SignalPhrases.Length);
                    score += SignalWeight * Math.Min(1.0, ratio);
                    reasons.Add($"Signal phrases matched {signalHits}");
                }
            }

            if (snapshot.ExpectedPageCountMin.HasValue || snapshot.ExpectedPageCountMax.HasValue)
            {
                maxScore += PageWeight;
                var inRange =
                    (!snapshot.ExpectedPageCountMin.HasValue || document.PageCount >= snapshot.ExpectedPageCountMin.Value) &&
                    (!snapshot.ExpectedPageCountMax.HasValue || document.PageCount <= snapshot.ExpectedPageCountMax.Value);
                if (inRange)
                {
                    score += PageWeight;
                    reasons.Add("Page count within expected range");
                }
            }

            if (snapshot.MimeTypesLower.Length > 0 && !string.IsNullOrWhiteSpace(mediaLower))
            {
                maxScore += MimeWeight;
                if (snapshot.MimeTypesLower.Any(m => string.Equals(m, mediaLower, StringComparison.OrdinalIgnoreCase)))
                {
                    score += MimeWeight;
                    reasons.Add("MIME type match");
                }
            }

            if (maxScore <= 0)
            {
                continue;
            }

            var confidence = score / maxScore;
            if (best is null || confidence > best.Value.Confidence)
            {
                var reason = reasons.Count > 0
                    ? $"Heuristic score {confidence:P0} ({string.Join(", ", reasons)})"
                    : $"Heuristic score {confidence:P0} (no direct matches)";
                best = new ClassificationResult(type.Id, confidence, ClassificationMethod.Heuristic, type.Version, reason);
            }
        }

        return best;
    }

    private async Task<ClassificationResult?> EvaluateVectorSimilarityAsync(
        SourceDocument document,
        string text,
        IReadOnlyList<SourceType> types,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var previewLength = Math.Clamp(_options.Classification.VectorPreviewLength, 200, 2000);
        var preview = text.Length > previewLength ? text[..previewLength] : text;
        if (string.IsNullOrWhiteSpace(preview))
        {
            return null;
        }

        float[] documentEmbedding;
        try
        {
            documentEmbedding = await Ai.Embed(preview, ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Embedding generation failed for document {DocumentId} during classification.", document.Id);
            return null;
        }

        var bestType = default(SourceType);
        var bestSimilarity = double.MinValue;

        foreach (var type in types)
        {
            var embedding = await GetTypeEmbeddingAsync(type, ct);
            if (embedding is null || embedding.Length == 0)
            {
                continue;
            }

            var similarity = CosineSimilarity(documentEmbedding, embedding);
            if (similarity > bestSimilarity)
            {
                bestSimilarity = similarity;
                bestType = type;
            }
        }

        if (bestType is null || bestSimilarity <= double.MinValue)
        {
            return null;
        }

        return new ClassificationResult(bestType.Id, bestSimilarity, ClassificationMethod.Vector, bestType.Version, $"Cosine similarity {bestSimilarity:P0} against type embedding");
    }

    private async Task<ClassificationResult?> EvaluateLlmAsync(
        SourceDocument document,
        string text,
        IReadOnlyList<SourceType> types,
        CancellationToken ct)
    {
        if (types.Count == 0 || string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var preview = text.Length > 500 ? text[..500] : text;
        var promptBuilder = new StringBuilder();
        promptBuilder.AppendLine("Classify the document into one of the following types and respond with JSON:");
        foreach (var type in types)
        {
            promptBuilder.Append("- ").Append(type.Id).Append(": ").AppendLine(type.Description);
        }

        promptBuilder.AppendLine().AppendLine("Document preview:").AppendLine(preview);
        promptBuilder.AppendLine().AppendLine("Respond ONLY in JSON with fields typeId, confidence (0.0-1.0), and reasoning.");

        var chatOptions = new AiChatOptions
        {
            Message = promptBuilder.ToString(),
            Temperature = 0.1,
            MaxTokens = 400,
            Model = _options.Classification.LlmModel ?? _options.Extraction.Model ?? "granite3.3:8b"
        };

        try
        {
            var response = await Ai.Chat(chatOptions, ct);

            // Check for empty response
            if (string.IsNullOrWhiteSpace(response))
            {
                _logger.LogWarning("LLM returned empty response for document {DocumentId} classification.", document.Id);
                return null;
            }

            // Strip markdown code fences if present (some LLMs wrap JSON in ```json blocks)
            var cleaned = response.Trim();
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

            // Validate looks like JSON
            if (!cleaned.StartsWith("{") && !cleaned.StartsWith("["))
            {
                _logger.LogWarning("LLM returned non-JSON response for document {DocumentId}: {Preview}",
                    document.Id, cleaned.Length > 100 ? cleaned.Substring(0, 100) + "..." : cleaned);
                return null;
            }

            var json = JObject.Parse(cleaned);

            var rawTypeId = json["typeId"]?.Value<string>()?.Trim();
            var confidence = json["confidence"]?.Value<double?>() ?? 0.5;
            confidence = Math.Clamp(confidence, 0.0, 1.0);

            SourceType? matched = null;
            if (!string.IsNullOrWhiteSpace(rawTypeId))
            {
                matched = types.FirstOrDefault(t => string.Equals(t.Id, rawTypeId, StringComparison.OrdinalIgnoreCase))
                          ?? types.FirstOrDefault(t => string.Equals(t.Name, rawTypeId, StringComparison.OrdinalIgnoreCase));
            }

            var reasoning = json["reasoning"]?.Value<string>() ?? "LLM response";

            matched ??= types.First();
            return new ClassificationResult(matched.Id, confidence, ClassificationMethod.Llm, matched.Version, reasoning);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "LLM returned invalid JSON for document {DocumentId} classification - will fallback to Unspecified type.", document.Id);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "LLM classification failed for document {DocumentId} - will fallback to Unspecified type.", document.Id);
            return null;
        }
    }

    private static string NormalizeContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        return content.ToLowerInvariant();
    }

    private static string NormalizeFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return string.Empty;
        }

        var lowered = fileName.ToLowerInvariant();
        return lowered
            .Replace("_", " ", StringComparison.Ordinal)
            .Replace("-", " ", StringComparison.Ordinal)
            .Replace(".", " ", StringComparison.Ordinal);
    }

    private static string NormalizePhrase(string phrase)
    {
        if (string.IsNullOrWhiteSpace(phrase))
        {
            return string.Empty;
        }

        var tokens = phrase
            .ToLowerInvariant()
            .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        return string.Join(' ', tokens);
    }

    private static int CountPhraseHits(string[] phrases, string textLower, string fileNameLower)
    {
        if (phrases.Length == 0)
        {
            return 0;
        }

        var hits = 0;

        foreach (var phrase in phrases)
        {
            if (phrase.Length == 0)
            {
                continue;
            }

            if ((textLower.Length > 0 && textLower.Contains(phrase, StringComparison.Ordinal)) ||
                (fileNameLower.Length > 0 && fileNameLower.Contains(phrase, StringComparison.Ordinal)))
            {
                hits++;
            }
        }

        return hits;
    }

    private SourceTypeSnapshot GetSnapshot(SourceType type)
    {
        return _snapshots.AddOrUpdate(
            type.Id,
            _ => CreateSnapshot(type),
            (_, existing) => existing.Version == type.Version && existing.UpdatedAt == type.UpdatedAt
                ? existing
                : CreateSnapshot(type));
    }

    private static SourceTypeSnapshot CreateSnapshot(SourceType type)
    {
        var descriptors = type.DescriptorHints
            .Select(NormalizePhrase)
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var signals = type.SignalPhrases
            .Select(NormalizePhrase)
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var mimeTypes = type.MimeTypes
            .Select(m => m.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new SourceTypeSnapshot(
            type.Version,
            type.UpdatedAt,
            descriptors,
            signals,
            mimeTypes,
            type.SupportsManualSelection,
            type.ExpectedPageCountMin,
            type.ExpectedPageCountMax);
    }

    private async Task<float[]?> GetTypeEmbeddingAsync(SourceType type, CancellationToken ct)
    {
        if (type.TypeEmbedding is { Length: > 0 } embedding && type.TypeEmbeddingVersion == type.Version)
        {
            _embeddingCache[type.Id] = embedding;
            return embedding;
        }

        if (_embeddingCache.TryGetValue(type.Id, out var cached) && cached.Length > 0 && type.TypeEmbeddingVersion == type.Version)
        {
            return cached;
        }

        try
        {
            var builder = new StringBuilder();
            builder.Append(type.Name).Append(". ").Append(type.Description);
            if (type.SignalPhrases.Count > 0)
            {
                builder.Append(" Signal phrases: ").Append(string.Join(", ", type.SignalPhrases));
            }

            var newEmbedding = await Ai.Embed(builder.ToString(), ct);
            type.TypeEmbedding = newEmbedding;
            type.TypeEmbeddingVersion = type.Version;
            type.TypeEmbeddingComputedAt = DateTime.UtcNow;
            type.TypeEmbeddingHash = TextHasher.Hash(builder.ToString());
            type.UpdatedAt = DateTime.UtcNow;
            await type.Save(ct);

            _embeddingCache[type.Id] = newEmbedding;
            return newEmbedding;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to compute embedding for source type {SourceType}.", type.Id);
            return null;
        }
    }

    private static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0)
        {
            return 0.0;
        }

        double dot = 0.0;
        double magA = 0.0;
        double magB = 0.0;

        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }

        var denominator = Math.Sqrt(magA) * Math.Sqrt(magB);
        return denominator <= 0 ? 0.0 : dot / denominator;
    }

    private static class TextHasher
    {
        public static string Hash(string text)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(text);
            var hash = sha.ComputeHash(bytes);
            return Convert.ToHexString(hash);
        }
    }
}
