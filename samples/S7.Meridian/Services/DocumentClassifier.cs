using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Koan.AI;
using Koan.AI.Contracts.Options;
using Koan.Data.Core;
using Koan.Samples.Meridian.Infrastructure;
using Koan.Samples.Meridian.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
        Regex[] FilenamePatterns,
        string[] KeywordsLower,
        string[] MimeTypesLower,
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
            return new ClassificationResult(MeridianConstants.SourceTypes.Unclassified, 0.0, ClassificationMethod.Manual, 1, "No source types configured.");
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

        _logger.LogDebug("Document {DocumentId} remained unclassified after cascade.", document.Id);
        return new ClassificationResult(MeridianConstants.SourceTypes.Unclassified, 0.0, ClassificationMethod.Heuristic, 1, "No classification strategy met confidence thresholds.");
    }

    private ClassificationResult? EvaluateHeuristics(SourceDocument document, string text, IReadOnlyList<SourceType> types)
    {
        var best = default(ClassificationResult?);
        var textLower = text.ToLowerInvariant();
        var mediaLower = document.MediaType?.ToLowerInvariant();

        foreach (var type in types)
        {
            var snapshot = GetSnapshot(type);
            double score = 0;
            double maxScore = 0;
            var reasons = new List<string>();

            if (snapshot.FilenamePatterns.Length > 0)
            {
                maxScore += 0.3;
                if (snapshot.FilenamePatterns.Any(pattern => pattern.IsMatch(document.OriginalFileName)))
                {
                    score += 0.3;
                    reasons.Add("Filename pattern match");
                }
            }

            if (snapshot.KeywordsLower.Length > 0 && textLower.Length > 0)
            {
                maxScore += 0.3;
                var matches = snapshot.KeywordsLower.Count(keyword => textLower.Contains(keyword));
                if (matches > 0)
                {
                    score += 0.3 * (matches / Math.Max(1.0, snapshot.KeywordsLower.Length));
                    reasons.Add($"Matched {matches} keyword(s)");
                }
            }

            if (snapshot.ExpectedPageCountMin.HasValue || snapshot.ExpectedPageCountMax.HasValue)
            {
                maxScore += 0.2;
                var inRange =
                    (!snapshot.ExpectedPageCountMin.HasValue || document.PageCount >= snapshot.ExpectedPageCountMin.Value) &&
                    (!snapshot.ExpectedPageCountMax.HasValue || document.PageCount <= snapshot.ExpectedPageCountMax.Value);
                if (inRange)
                {
                    score += 0.2;
                    reasons.Add("Page count within expected range");
                }
            }

            if (snapshot.MimeTypesLower.Length > 0 && !string.IsNullOrWhiteSpace(mediaLower))
            {
                maxScore += 0.2;
                if (snapshot.MimeTypesLower.Any(m => string.Equals(m, mediaLower, StringComparison.OrdinalIgnoreCase)))
                {
                    score += 0.2;
                    reasons.Add("Mime type match");
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
            documentEmbedding = await Ai.Embed(preview, ct).ConfigureAwait(false);
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
            var embedding = await GetTypeEmbeddingAsync(type, ct).ConfigureAwait(false);
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
            var response = await Ai.Chat(chatOptions, ct).ConfigureAwait(false);
            var json = JObject.Parse(response);

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
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "LLM classification failed for document {DocumentId}.", document.Id);
            return null;
        }
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
        var patterns = type.FilenamePatterns
            .Select(pattern => new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled))
            .ToArray();

        var keywords = type.Keywords
            .Select(keyword => keyword.ToLowerInvariant())
            .ToArray();

        var mimeTypes = type.MimeTypes
            .Select(m => m.ToLowerInvariant())
            .ToArray();

        return new SourceTypeSnapshot(
            type.Version,
            type.UpdatedAt,
            patterns,
            keywords,
            mimeTypes,
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
            if (type.Keywords.Count > 0)
            {
                builder.Append(" Keywords: ").Append(string.Join(", ", type.Keywords));
            }

            var newEmbedding = await Ai.Embed(builder.ToString(), ct).ConfigureAwait(false);
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
